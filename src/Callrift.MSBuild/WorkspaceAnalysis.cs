using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Callrift.Core;
using Microsoft.Build.Construction;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using BuildProjectCollection = Microsoft.Build.Evaluation.ProjectCollection;

namespace Callrift.MSBuild;

public static class WorkspaceAnalysis
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<CallGraph> AnalyzeAsync(WorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        using var loaded = await LoadAsync(request, cancellationToken);
        return await loaded.AnalyzeAsync(request.IncludeTests, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<LoadedWorkspace> LoadAsync(WorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var properties = new Dictionary<string, string> { ["Configuration"] = request.Options.Configuration };
        var target = Path.Combine(request.Root, request.Options.Target);
        var referenceOutputs = await PrepareReferencesAsync(request, target, properties, cancellationToken);
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies.Concat([typeof(CSharpFormattingOptions).Assembly]));
        var workspace = MSBuildWorkspace.Create(properties, host);
        try
        {
            workspace.LoadMetadataForReferencedProjects = false;
            workspace.SkipUnrecognizedProjects = true;
            Solution solution;
            if (target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                solution = (await workspace.OpenProjectAsync(target, cancellationToken: cancellationToken)).Solution;
            else
                solution = await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken);
            var failures = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToArray();
            if (failures.Length > 0) throw new InvalidOperationException("Workspace loading failed:\n" + string.Join("\n", failures.Select(d => d.Message)));
            var loaded = new List<LoadedProject>();
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
                loaded.Add(new LoadedProject(project.Id, project.FilePath!, framework, isTest || HasTestFramework(project)));
            }
            var roots = target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? loaded.Where(p => Path.GetFullPath(p.Path) == Path.GetFullPath(target))
                : loaded;
            var selected = new HashSet<ProjectId>();
            var unmatched = new List<(string Path, ProjectId[] Candidates, string[] Frameworks)>();
            foreach (var group in roots.GroupBy(p => p.Path))
            {
                var candidates = group.ToArray();
                if (request.Options.Framework is null && candidates.Length > 1)
                    throw new InvalidOperationException("Project targets multiple frameworks; select --framework.");
                var matches = request.Options.Framework is null ? candidates : candidates.Where(p => p.Framework == request.Options.Framework).ToArray();
                if (matches.Length == 0 && !target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && candidates.Length == 1)
                    matches = candidates;
                if (matches.Length == 0 && !target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    unmatched.Add((group.Key!, candidates.Select(p => p.Id).ToArray(), candidates.Select(p => p.Framework).ToArray()));
                    continue;
                }
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Cannot select framework '{request.Options.Framework}' for {group.Key}; available: {string.Join(", ", candidates.Select(p => p.Framework))}.");
                selected.Add(matches[0].Id);
            }
            var pending = new Queue<ProjectId>(selected);
            while (pending.TryDequeue(out var id))
            {
                var project = solution.GetProject(id)!;
                var groups = project.ProjectReferences.GroupBy(r => solution.GetProject(r.ProjectId)!.FilePath).ToArray();
                if (groups.Any(g => g.Count() > 1))
                {
                    var framework = loaded.Single(p => p.Id == id).Framework;
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
            solution = await PreserveAdditionalFileOrderAsync(solution, selected, cancellationToken);
            return new LoadedWorkspace(request, workspace, solution, loaded, selected);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task<Solution> PreserveAdditionalFileOrderAsync(Solution solution, IReadOnlySet<ProjectId> selected, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects.Where(project => selected.Contains(project.Id)))
        {
            var documents = project.AdditionalDocuments.ToArray();
            if (documents.Length == 0) continue;
            var ordered = ImmutableArray.CreateBuilder<DocumentInfo>(documents.Length);
            for (var index = 0; index < documents.Length; index++)
            {
                var document = documents[index];
                var text = await document.GetTextAsync(cancellationToken);
                var id = DocumentId.CreateFromSerialized(project.Id, new Guid(index + 1, 0, 0, new byte[8]));
                ordered.Add(DocumentInfo.Create(id, document.Name, document.Folders,
                    loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), document.FilePath)), filePath: document.FilePath));
            }
            solution = solution.RemoveAdditionalDocuments(documents.Select(document => document.Id).ToImmutableArray())
                .AddAdditionalDocuments(ordered.MoveToImmutable());
        }
        return solution;
    }

    private static async Task<CallGraph> AnalyzeLoadedAsync(WorkspaceRequest request, MSBuildWorkspace workspace,
        Solution solution, IReadOnlyList<LoadedProject> loaded, IReadOnlySet<ProjectId> selected, CancellationToken cancellationToken)
    {
        var projects = new List<(Project Project, CSharpCompilation Compilation, string Scope)>();
        foreach (var item in loaded.Where(p => selected.Contains(p.Id)))
        {
            var project = solution.GetProject(item.Id)!;
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
        foreach (var project in projects)
            foreach (var reference in project.Compilation.References.OfType<CompilationReference>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byAssembly.TryGetValue(reference.Compilation.Assembly, out var scope)
                    || project.Compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol projected) continue;
                if (byAssembly.TryGetValue(projected, out var existing) && existing != scope)
                    throw new InvalidOperationException($"Ambiguous project identity for referenced assembly {projected.Name}.");
                byAssembly[projected] = scope;
            }
        string Scope(ISymbol symbol)
        {
            if (byAssembly.TryGetValue(symbol.ContainingAssembly, out var known)) return known;
            var candidates = projects.Where(p => p.Compilation.AssemblyName == symbol.ContainingAssembly.Name).ToArray();
            if (candidates.Length == 1) return candidates[0].Scope;
            if (candidates.Length > 1)
            {
                var paths = symbol.DeclaringSyntaxReferences.Select(r => r.SyntaxTree.FilePath).ToHashSet(StringComparer.Ordinal);
                var owning = candidates.Where(p => p.Compilation.SyntaxTrees.Any(t => paths.Contains(t.FilePath))).ToArray();
                if (owning.Length == 1) return owning[0].Scope;
                throw new InvalidOperationException($"Cannot disambiguate project identity for {symbol.ToDisplayString()}.");
            }
            return "metadata:" + symbol.ContainingAssembly.Name;
        }
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        var types = new List<INamedTypeSymbol>();
        var typeDefinitions = new Dictionary<string, DispatchTypeDefinition>(StringComparer.Ordinal);
        var declaringPaths = new Dictionary<(string Scope, string Path), string>();
        var diagnostics = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Warning)
            .Where(d => d is not ProjectDiagnostic projectDiagnostic || selected.Contains(projectDiagnostic.ProjectId))
            .Select(d => new AnalysisDiagnostic("workspace-warning", MSBuildAnalysisProvider.CleanMessage(
                loaded.Aggregate(d.Message, (message, item) => message.Replace(item.Path,
                    Path.GetRelativePath(request.Root, item.Path).Replace('\\', '/'), StringComparison.Ordinal)), request.Root))).ToList();
        foreach (var item in projects)
        {
            string LogicalPath(string path)
            {
                if (Path.IsPathRooted(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(request.Root) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return Path.GetRelativePath(request.Root, path).Replace('\\', '/');
                return "generated/" + Path.GetRelativePath(request.Root, item.Project.FilePath!).Replace('\\', '/') + "/" + string.Join('/', path.Replace('\\', '/').Split('/').TakeLast(3));
            }
            var graph = SourceOnlyAnalysisProvider.AnalyzeCompilation(item.Compilation, syntaxTrees: null, scope: Scope, logicalPath: LogicalPath,
                includeBodyFingerprints: true, cancellationToken: cancellationToken, typeScope: Scope);
            foreach (var member in graph.Members) members.Add(member.Key, member.Value);
            foreach (var definition in graph.TypeDefinitions) typeDefinitions[definition.Key] = definition.Value;
            diagnostics.AddRange(graph.Diagnostics);
            foreach (var tree in item.Compilation.SyntaxTrees)
            {
                declaringPaths[(item.Scope, tree.FilePath)] = LogicalPath(tree.FilePath);
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
        var typesByAssembly = new Dictionary<IAssemblySymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var type in types)
        {
            if (!typesByAssembly.TryGetValue(type.ContainingAssembly, out var declared)) typesByAssembly[type.ContainingAssembly] = declared = [];
            declared.Add(type);
        }
        foreach (var item in projects)
            foreach (var reference in item.Compilation.References.OfType<CompilationReference>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!typesByAssembly.TryGetValue(reference.Compilation.Assembly, out var declared)
                    || item.Compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol projected
                    || ReferenceEquals(projected, reference.Compilation.Assembly)) continue;
                foreach (var type in declared)
                    if (projected.GetTypeByMetadataName(MetadataName(type)) is { } referenced) types.Add(referenced);
            }
        var dispatch = SourceOnlyAnalysisProvider.BuildDispatchMap(types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default), members, Scope,
            (method, path) => declaringPaths.TryGetValue((Scope(method), path), out var logical) ? logical
                : throw new InvalidOperationException($"Cannot identify the declaring file for {method.ToDisplayString()}."),
            (type, path) => declaringPaths.TryGetValue((Scope(type), path), out var logical) ? logical
                : throw new InvalidOperationException($"Cannot identify the declaring file for {type.ToDisplayString()}."), cancellationToken);
        foreach (var definition in dispatch.TypeDefinitions) typeDefinitions[definition.Key] = definition.Value;
        return new CallGraph(members, dispatch.Implementations, diagnostics.Distinct().OrderBy(d => d.Location?.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location?.Line).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray())
        { Coverage = MSBuildAnalysisProvider.WorkspaceCoverage, DispatchContracts = dispatch.Contracts, TypeDefinitions = typeDefinitions };
    }

    private static string MetadataName(INamedTypeSymbol type) => type.ContainingType is { } containing
        ? MetadataName(containing) + "+" + type.MetadataName
        : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;

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
        await NormalizeFrameworkListsAsync(request, paths, properties, cancellationToken);
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

    private static async Task NormalizeFrameworkListsAsync(WorkspaceRequest request, IEnumerable<string> paths,
        Dictionary<string, string> properties, CancellationToken cancellationToken)
    {
        var pending = new Queue<string>(paths);
        var visited = new HashSet<string>(PathComparer);
        using var collection = new BuildProjectCollection(properties);
        while (pending.TryDequeue(out var path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (!visited.Add(path) || !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;
            var project = collection.LoadProject(path);
            var declared = project.GetPropertyValue("TargetFrameworks");
            var frameworks = declared.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (declared.Length > 0 && declared.Split(';').Any(string.IsNullOrWhiteSpace))
            {
                var directory = Path.GetFullPath(project.GetPropertyValue("MSBuildProjectExtensionsPath"), Path.GetDirectoryName(path)!);
                if (!directory.StartsWith(Path.GetFullPath(request.Root) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("Framework normalization requires an intermediate directory inside the workspace.");
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, Path.GetFileName(path) + ".callrift.targets"), """
                    <Project>
                        <PropertyGroup>
                            <TargetFrameworks>$([System.Text.RegularExpressions.Regex]::Replace('$(TargetFrameworks)', '(?:\s*;\s*)+', ';').Trim(';').Trim())</TargetFrameworks>
                        </PropertyGroup>
                    </Project>
                    """, cancellationToken);
            }
            foreach (var framework in frameworks.Length == 0 ? [project.GetPropertyValue("TargetFramework")] : frameworks)
            {
                if (framework.Length > 0)
                {
                    project.SetGlobalProperty("TargetFramework", framework);
                    project.ReevaluateIfNecessary();
                }
                foreach (var reference in project.GetItems("ProjectReference"))
                    pending.Enqueue(reference.GetMetadataValue("FullPath"));
            }
            collection.UnloadProject(project);
        }
    }

    private static async Task<HashSet<string>> ResolveReferenceOutputsAsync(WorkspaceRequest request, string path, string framework, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var properties = new Dictionary<string, string> { ["Configuration"] = request.Options.Configuration };
        if (framework.Length > 0 && framework != "default") properties["TargetFramework"] = framework;
        using var collection = new BuildProjectCollection(properties);
        using var manager = new BuildManager();
        var log = new ReferenceBuildLog();
        var parameters = new BuildParameters(collection)
        {
            EnableNodeReuse = false,
            SaveOperatingEnvironment = true,
            ShutdownInProcNodeOnBuildFinish = true,
            MaxNodeCount = 1,
            Loggers = [log]
        };
        var data = new BuildRequestData(path, properties, null, ["ResolveReferences"], null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);
        var completion = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        BuildResult result;
        manager.BeginBuild(parameters);
        try
        {
            var submission = manager.PendBuildRequest(data);
            submission.ExecuteAsync(completed => completion.TrySetResult(completed.BuildResult), null);
            using var registration = cancellationToken.Register(manager.CancelAllSubmissions);
            result = await completion.Task;
        }
        finally
        {
            manager.EndBuild();
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (result.OverallResult != BuildResultCode.Success)
            throw new InvalidOperationException("MSBuild analysis failed:\n" + MSBuildAnalysisProvider.CleanMessage(
                string.Join("\n", log.Messages.Append(result.Exception?.Message).Where(message => !string.IsNullOrEmpty(message))), request.Root).Trim());
        var state = result.ProjectStateAfterBuild ?? throw new InvalidOperationException("MSBuild returned no reference project state.");
        return state.GetItems("_ResolvedProjectReferencePaths").Select(item => Path.GetFullPath(item.GetMetadataValue("FullPath"))).ToHashSet(PathComparer);
    }

    private sealed class ReferenceBuildLog : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;
        public string? Parameters { get; set; }

        public void Initialize(IEventSource eventSource)
        {
            eventSource.ErrorRaised += (_, error) => Messages.Enqueue($"{error.File}({error.LineNumber},{error.ColumnNumber}): error {error.Code}: {error.Message}");
            eventSource.WarningRaised += (_, warning) => Messages.Enqueue($"{warning.File}({warning.LineNumber},{warning.ColumnNumber}): warning {warning.Code}: {warning.Message}");
        }

        public void Shutdown() { }
    }

    internal sealed record LoadedProject(ProjectId Id, string Path, string Framework, bool IsTest);

    internal sealed class LoadedWorkspace(WorkspaceRequest request, MSBuildWorkspace workspace, Solution solution,
        IReadOnlyList<LoadedProject> loaded, IReadOnlySet<ProjectId> selected) : IDisposable
    {
        private Solution current = solution;
        private bool disposed;

        public bool Matches(WorkspaceRequest candidate) => request.Root == candidate.Root && request.Options == candidate.Options;

        public async Task RefreshSourceTextsAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var updated = current;
            foreach (var project in current.Projects)
                foreach (var document in project.Documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = document.FilePath ?? throw new InvalidOperationException("A loaded source document has no file path.");
                    if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(request.Root) + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException("Prepared source documents must remain inside the fixture root.");
                    var content = await File.ReadAllTextAsync(path, cancellationToken);
                    var previous = await document.GetTextAsync(cancellationToken);
                    if (previous.ToString() == content) continue;
                    updated = updated.WithDocumentText(document.Id, SourceText.From(content, previous.Encoding ?? new UTF8Encoding(false)));
                }
            current = updated;
        }

        public Task<CallGraph> AnalyzeAsync(bool includeTests, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return AnalyzeLoadedAsync(request with { IncludeTests = includeTests }, workspace, current, loaded, selected, cancellationToken);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            workspace.Dispose();
        }
    }
}
