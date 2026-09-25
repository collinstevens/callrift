using System.Runtime.CompilerServices;
using System.Text.Json;
using Callrift.Core;
using Microsoft.Build.Construction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using BuildProjectCollection = Microsoft.Build.Evaluation.ProjectCollection;

namespace Callrift.MSBuild;

public static class WorkspaceAnalysis
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<CallGraph> AnalyzeAsync(WorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var properties = new Dictionary<string, string> { ["Configuration"] = request.Options.Configuration };
        var target = Path.Combine(request.Root, request.Options.Target);
        var referenceOutputs = await PrepareReferencesAsync(request, target, properties, cancellationToken);
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies.Concat([typeof(CSharpFormattingOptions).Assembly]));
        using var workspace = MSBuildWorkspace.Create(properties, host);
        workspace.LoadMetadataForReferencedProjects = false;
        workspace.SkipUnrecognizedProjects = true;
        Solution solution;
        if (target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            solution = (await workspace.OpenProjectAsync(target, cancellationToken: cancellationToken)).Solution;
        else
            solution = await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken);
        var failures = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToArray();
        if (failures.Length > 0) throw new InvalidOperationException("Workspace loading failed:\n" + string.Join("\n", failures.Select(d => d.Message)));
        var loaded = new List<(Project Project, string Framework, bool IsTest)>();
        using var evaluatedProjects = new BuildProjectCollection(new Dictionary<string, string> { ["Configuration"] = request.Options.Configuration });
        foreach (var project in solution.Projects.OrderBy(p => p.FilePath, StringComparer.Ordinal))
        {
            if (project.Language != LanguageNames.CSharp) continue;
            var evaluated = evaluatedProjects.LoadProject(project.FilePath!);
            var frameworks = evaluated.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries);
            project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.TargetFramework", out var framework);
            if (string.IsNullOrEmpty(framework)) framework = evaluated.GetPropertyValue("TargetFramework");
            if (framework.Length == 0 && frameworks.Length == 1) framework = frameworks[0];
            if (framework.Length == 0 && frameworks.Length > 1)
                framework = frameworks.SingleOrDefault(f => project.Name.EndsWith("(" + f + ")", StringComparison.Ordinal)) ?? "";
            if (framework.Length == 0 && frameworks.Length > 1)
                throw new InvalidOperationException($"Cannot identify the loaded target framework for {project.Name}.");
            if (framework.Length == 0) framework = "default";
            else
            {
                evaluated.SetGlobalProperty("TargetFramework", framework);
                evaluated.ReevaluateIfNecessary();
            }
            var isTest = evaluated.GetPropertyValue("IsTestProject").Equals("true", StringComparison.OrdinalIgnoreCase);
            evaluatedProjects.UnloadProject(evaluated);
            loaded.Add((project, framework, isTest || HasTestFramework(project)));
        }
        var roots = target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? loaded.Where(p => Path.GetFullPath(p.Project.FilePath!) == Path.GetFullPath(target))
            : loaded;
        var selected = new HashSet<ProjectId>();
        var unmatched = new List<(string Path, ProjectId[] Candidates, string[] Frameworks)>();
        foreach (var group in roots.GroupBy(p => p.Project.FilePath))
        {
            var candidates = group.ToArray();
            if (request.Options.Framework is null && candidates.Length > 1)
                throw new InvalidOperationException("Project targets multiple frameworks; select --framework.");
            var matches = request.Options.Framework is null ? candidates : candidates.Where(p => p.Framework == request.Options.Framework).ToArray();
            if (matches.Length == 0 && !target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && candidates.Length == 1)
                matches = candidates;
            if (matches.Length == 0 && !target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                unmatched.Add((group.Key!, candidates.Select(p => p.Project.Id).ToArray(), candidates.Select(p => p.Framework).ToArray()));
                continue;
            }
            if (matches.Length != 1)
                throw new InvalidOperationException($"Cannot select framework '{request.Options.Framework}' for {group.Key}; available: {string.Join(", ", candidates.Select(p => p.Framework))}.");
            selected.Add(matches[0].Project.Id);
        }
        var pending = new Queue<ProjectId>(selected);
        while (pending.TryDequeue(out var id))
        {
            var project = solution.GetProject(id)!;
            var groups = project.ProjectReferences.GroupBy(r => solution.GetProject(r.ProjectId)!.FilePath).ToArray();
            if (groups.Any(g => g.Count() > 1))
            {
                var framework = loaded.Single(p => p.Project.Id == id).Framework;
                var key = ReferenceKey(project.FilePath!, framework);
                if (!referenceOutputs.TryGetValue(key, out var outputs))
                    referenceOutputs[key] = outputs = await ResolveReferenceOutputsAsync(request, project.FilePath!, framework, cancellationToken);
                var references = new List<ProjectReference>();
                foreach (var group in groups)
                {
                    var matches = group.Count() == 1 ? group.ToArray() : group.Where(r =>
                    {
                        var dependency = solution.GetProject(r.ProjectId)!;
                        return dependency.OutputFilePath is { } output && outputs.Contains(Path.GetFullPath(output))
                            || dependency.OutputRefFilePath is { } outputRef && outputs.Contains(Path.GetFullPath(outputRef));
                    }).ToArray();
                    if (matches.Length == 0)
                        throw new InvalidOperationException($"Cannot match the resolved framework for project reference {group.Key} from {project.Name}.");
                    references.AddRange(matches);
                }
                solution = solution.WithProjectReferences(id, references);
                project = solution.GetProject(id)!;
            }
            foreach (var reference in project.ProjectReferences)
                if (selected.Add(reference.ProjectId)) pending.Enqueue(reference.ProjectId);
        }
        foreach (var group in unmatched)
            if (!group.Candidates.Any(selected.Contains))
                throw new InvalidOperationException($"Cannot select framework '{request.Options.Framework}' for {group.Path}; available: {string.Join(", ", group.Frameworks)}.");
        var projects = new List<(Project Project, CSharpCompilation Compilation, string Scope)>();
        foreach (var item in loaded.Where(p => selected.Contains(p.Project.Id)))
        {
            var project = solution.GetProject(item.Project.Id)!;
            var framework = item.Framework;
            var isTest = item.IsTest;
            if (!request.IncludeTests && isTest) continue;
            var compilation = await project.GetCompilationAsync(cancellationToken) as CSharpCompilation
                ?? throw new InvalidOperationException($"No C# compilation for {project.Name}.");
            projects.Add((project, compilation, "project:" + Path.GetRelativePath(request.Root, project.FilePath!).Replace('\\', '/') + "@" + framework));
        }
        if (projects.Count == 0) throw new InvalidOperationException("The workspace contains no included C# projects.");
        var byAssembly = new Dictionary<IAssemblySymbol, string>(ReferenceEqualityComparer.Instance);
        foreach (var project in projects) byAssembly.Add(project.Compilation.Assembly, project.Scope);
        string Scope(IMethodSymbol method)
        {
            if (byAssembly.TryGetValue(method.ContainingAssembly, out var known)) return known;
            var candidates = projects.Where(p => p.Compilation.AssemblyName == method.ContainingAssembly.Name).ToArray();
            if (candidates.Length == 1) return candidates[0].Scope;
            if (candidates.Length > 1)
            {
                var paths = method.DeclaringSyntaxReferences.Select(r => r.SyntaxTree.FilePath).ToHashSet(StringComparer.Ordinal);
                var owning = candidates.Where(p => p.Compilation.SyntaxTrees.Any(t => paths.Contains(t.FilePath))).ToArray();
                if (owning.Length == 1) return owning[0].Scope;
                throw new InvalidOperationException($"Cannot disambiguate project identity for {method.ToDisplayString()}.");
            }
            return "metadata:" + method.ContainingAssembly.Name;
        }
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        var types = new List<INamedTypeSymbol>();
        var diagnostics = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Warning)
            .Where(d => d is not ProjectDiagnostic projectDiagnostic || selected.Contains(projectDiagnostic.ProjectId))
            .Select(d => new AnalysisDiagnostic("workspace-warning", MSBuildAnalysisProvider.CleanMessage(
                loaded.Aggregate(d.Message, (message, item) => message.Replace(item.Project.FilePath!,
                    Path.GetRelativePath(request.Root, item.Project.FilePath!).Replace('\\', '/'), StringComparison.Ordinal)), request.Root))).ToList();
        foreach (var item in projects)
        {
            string LogicalPath(string path)
            {
                if (Path.IsPathRooted(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(request.Root) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return Path.GetRelativePath(request.Root, path).Replace('\\', '/');
                return "generated/" + Path.GetRelativePath(request.Root, item.Project.FilePath!).Replace('\\', '/') + "/" + string.Join('/', path.Replace('\\', '/').Split('/').TakeLast(3));
            }
            var graph = SourceOnlyAnalysisProvider.AnalyzeCompilation(item.Compilation, scope: Scope, logicalPath: LogicalPath,
                includeBodyFingerprints: true, cancellationToken: cancellationToken);
            foreach (var member in graph.Members) members.Add(member.Key, member.Value);
            diagnostics.AddRange(graph.Diagnostics);
            foreach (var tree in item.Compilation.SyntaxTrees)
            {
                var model = item.Compilation.GetSemanticModel(tree);
                types.AddRange(tree.GetRoot(cancellationToken).DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Select(t => model.GetDeclaredSymbol(t, cancellationToken)).OfType<INamedTypeSymbol>());
            }
            foreach (var diagnostic in item.Compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error || d.Id is "CS8784" or "CS8785"))
            {
                var span = diagnostic.Location.GetLineSpan();
                var location = diagnostic.Location.IsInSource ? new SourceLocation(LogicalPath(span.Path), span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1, span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1) : null;
                diagnostics.Add(new AnalysisDiagnostic(diagnostic.Id, MSBuildAnalysisProvider.CleanMessage(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), request.Root), location));
            }
        }
        var implementations = SourceOnlyAnalysisProvider.BuildImplementationMap(types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default), members, Scope, cancellationToken);
        return new CallGraph(members, implementations, diagnostics.Distinct().OrderBy(d => d.Location?.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location?.Line).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray())
        { Coverage = MSBuildAnalysisProvider.WorkspaceCoverage };
    }

    private static bool HasTestFramework(Project project)
    {
        return project.MetadataReferences.Any(r => Path.GetFileNameWithoutExtension(r.Display)?.ToLowerInvariant()
            is "xunit.core" or "xunit.v3.core" or "nunit.framework" or "microsoft.visualstudio.testplatform.testframework");
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string ReferenceKey(string path, string framework) => Path.GetFullPath(path) + "\0" + framework;

    private static async Task<Dictionary<string, HashSet<string>>> PrepareReferencesAsync(WorkspaceRequest request, string target, Dictionary<string, string> properties, CancellationToken cancellationToken)
    {
        var outputs = new Dictionary<string, HashSet<string>>(PathComparer);
        var singleProject = target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        var paths = singleProject ? [target] : SolutionFile.Parse(target).ProjectsInOrder
            .Select(p => p.AbsolutePath).Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        using var collection = new BuildProjectCollection(properties);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = collection.LoadProject(path);
            var frameworks = project.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries);
            var framework = project.GetPropertyValue("TargetFramework");
            if (frameworks.Length == 0 && framework.Length > 0) frameworks = [framework];
            collection.UnloadProject(project);
            if (request.Options.Framework is null && frameworks.Length > 1)
                throw new InvalidOperationException("Project targets multiple frameworks; select --framework.");
            if (request.Options.Framework is not null && frameworks.Contains(request.Options.Framework)) framework = request.Options.Framework;
            else if (!singleProject && frameworks.Length > 1) continue;
            else if (request.Options.Framework is not null && singleProject && !frameworks.Contains(request.Options.Framework))
                throw new InvalidOperationException($"Project does not target framework '{request.Options.Framework}': {path}.");
            else if (frameworks.Length == 1) framework = frameworks[0];
            outputs[ReferenceKey(path, framework.Length == 0 ? "default" : framework)] = await ResolveReferenceOutputsAsync(request, path, framework, cancellationToken);
        }
        return outputs;
    }

    private static async Task<HashSet<string>> ResolveReferenceOutputsAsync(WorkspaceRequest request, string path, string framework, CancellationToken cancellationToken)
    {
        var resultPath = Path.Combine(Path.GetDirectoryName(request.Root)!, "references-" + Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string>
        {
            "msbuild", path, "-target:ResolveReferences", "-getItem:_ResolvedProjectReferencePaths",
            "-getResultOutputFile:" + resultPath,
            "-property:Configuration=" + request.Options.Configuration, "-nologo", "-verbosity:quiet"
        };
        if (framework.Length > 0 && framework != "default") arguments.Add("-property:TargetFramework=" + framework);
        try
        {
            await MSBuildAnalysisProvider.RunProcessAsync(Path.GetDirectoryName(path)!, arguments, request.Root, cancellationToken);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
            return document.RootElement.GetProperty("Items").GetProperty("_ResolvedProjectReferencePaths").EnumerateArray()
                .Select(item => Path.GetFullPath(item.GetProperty("FullPath").GetString()!)).ToHashSet(PathComparer);
        }
        finally
        {
            File.Delete(resultPath);
        }
    }
}
