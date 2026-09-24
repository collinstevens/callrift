using System.Runtime.CompilerServices;
using Callrift.Core;
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
        if (request.Options.Framework is not null) properties["TargetFramework"] = request.Options.Framework;
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies.Concat([typeof(CSharpFormattingOptions).Assembly]));
        using var workspace = MSBuildWorkspace.Create(properties, host);
        workspace.LoadMetadataForReferencedProjects = false;
        workspace.SkipUnrecognizedProjects = true;
        var target = Path.Combine(request.Root, request.Options.Target);
        Solution solution;
        if (target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            solution = (await workspace.OpenProjectAsync(target, cancellationToken: cancellationToken)).Solution;
        else
            solution = await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken);
        var failures = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToArray();
        if (failures.Length > 0) throw new InvalidOperationException("Workspace loading failed:\n" + string.Join("\n", failures.Select(d => d.Message)));
        var projects = new List<(Project Project, CSharpCompilation Compilation, string Scope)>();
        using var evaluatedProjects = new BuildProjectCollection(properties);
        foreach (var project in solution.Projects.OrderBy(p => p.FilePath, StringComparer.Ordinal))
        {
            if (project.Language != LanguageNames.CSharp) continue;
            var evaluated = evaluatedProjects.LoadProject(project.FilePath!);
            var isTest = evaluated.GetPropertyValue("IsTestProject").Equals("true", StringComparison.OrdinalIgnoreCase);
            var frameworks = evaluated.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (request.Options.Framework is null && frameworks.Length > 1) throw new InvalidOperationException("Project targets multiple frameworks; select --framework.");
            var framework = evaluated.GetPropertyValue("TargetFramework");
            if (framework.Length == 0) framework = frameworks.FirstOrDefault() ?? "default";
            evaluatedProjects.UnloadProject(evaluated);
            if (!request.IncludeTests && (isTest || HasTestFramework(project))) continue;
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
        var diagnostics = new List<AnalysisDiagnostic>();
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
}
