using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.MSBuild;
using Callrift.RealWorldCases;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[EvaluateOverhead(false)]
public class WorkspaceStageBenchmarks
{
    private string scratch = "";
    private WorkspaceRequest request = null!;
    private WorkspaceAnalysis.LoadedWorkspace? measuredWorkspace;
    private IReadOnlyList<Project> projects = [];
    private CSharpCompilation[]? measuredCompilations;
    private ProjectSnapshot[] expected = [];
    private GeneratorInput[] inputs = [];
    private GeneratorDriver[] drivers = [];
    private CSharpCompilation[] generatorCompilations = [];
    private GeneratorDriverRunResult[]? measuredGenerators;
    private bool randomInterceptorNames;

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-esmodule-localization")]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public Task Setup()
    {
        if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
        return Prepare();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task Prepare()
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(item => item.Id == Case);
        randomInterceptorNames = Case == "orchardcore-esmodule-localization" && entry.After == "4c1d10e68dd443c8bc9fc8d8a02059081fc6be12";
        var repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        var snapshot = await repository.ReadSnapshotAsync(entry.After, allFiles: true);
        var (target, framework) = Case switch
        {
            "serilog-alignment-guard" => ("src/Serilog/Serilog.csproj", "net10.0"),
            "polly-secondary-action" => ("src/Polly.Extensions/Polly.Extensions.csproj", "net8.0"),
            "orchardcore-esmodule-localization" => ("src/OrchardCore.Cms.Web/OrchardCore.Cms.Web.csproj", "net10.0"),
            _ => throw new InvalidOperationException("Unknown workspace workload.")
        };
        scratch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Callrift", "benchmark-workspaces", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(scratch, "tree");
        await MSBuildAnalysisProvider.MaterializeAsync(snapshot, root);
        await MSBuildAnalysisProvider.RestoreAsync(root, Path.Combine(root, target), "Debug");
        request = new WorkspaceRequest(root, new MSBuildOptions(target, framework, NoRestore: true), false, "");
        using var loaded = await WorkspaceAnalysis.LoadAsync(request);
        var included = loaded.IncludedProjects(false);
        if (included.Count == 0) throw new InvalidOperationException("No included projects in the workspace workload.");
        var snapshots = new List<ProjectSnapshot>();
        var generatorInputs = new List<GeneratorInput>();
        foreach (var project in included)
        {
            var compilation = await Compile(project);
            snapshots.Add(await Capture(project, compilation));
            var generators = project.AnalyzerReferences.SelectMany(reference => reference.GetGenerators(LanguageNames.CSharp)).ToArray();
            if (generators.Length == 0) continue;
            var generated = await project.GetSourceGeneratedDocumentsAsync();
            var generatedTrees = new List<SyntaxTree>();
            var generatedSources = new List<GeneratedSource>();
            foreach (var document in generated)
            {
                generatedTrees.Add(await document.GetSyntaxTreeAsync() ?? throw new InvalidOperationException("Generated document has no syntax tree."));
                generatedSources.Add(new GeneratedSource(document.Name, (await document.GetTextAsync()).ToString()));
            }
            var texts = new List<AdditionalText>();
            foreach (var document in project.AdditionalDocuments)
                texts.Add(new FrozenAdditionalText(document.FilePath!, await document.GetTextAsync()));
            generatorInputs.Add(new GeneratorInput(project.Name, compilation.RemoveSyntaxTrees(generatedTrees), generators, texts.ToArray(),
                (CSharpParseOptions)project.ParseOptions!, project.AnalyzerOptions.AnalyzerConfigOptionsProvider, snapshots[^1].Generated, generatedSources.ToArray()));
        }
        expected = snapshots.ToArray();
        inputs = generatorInputs.ToArray();
        if (inputs.Length == 0 || expected.Sum(project => project.Generated.Length) == 0)
            throw new InvalidOperationException("The selected workload does not execute source generators.");
        ResetGenerators();
        RunGenerators();
        CheckGenerators();
        var evidence = Path.Combine(RealWorldCaseStore.FindRoot(), "artifacts", "benchmark-workspaces", $"{Case}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "workload.json"), JsonSerializer.Serialize(new
        {
            Case,
            entry.Repository,
            entry.Before,
            entry.After,
            Target = target,
            Framework = framework,
            IncludeTests = false,
            Projects = expected,
            GeneratorProjects = inputs.Select(input => new { input.Name, Generators = input.Generators.Select(generator => generator.GetType().FullName).ToArray() }),
            CacheCondition = "Prewarmed package, filesystem and analyzer assembly caches. Each workspace-open invocation creates a new workspace; compilation iterations open a fresh workspace outside timing. Generator iterations use fresh drivers and source compilations with pre-parsed trees, compiled project references and prepared additional texts; no previous generator driver or source binding cache is reused.",
            StageScope = "OpenWorkspace includes production reference preparation, design-time builds, framework selection and additional-file ordering; it excludes materialization, restore and compilation. CompileWithGenerators includes workspace compilation construction, dependency compilation and generation; workspace loading is outside timing. RunGenerators includes driver execution on already compiled project references; it excludes parsing, reference construction, workspace loading and dependency generation. These scopes overlap and must not be summed.",
            AllocationScope = "MemoryDiagnoser measures allocations in the benchmark host. Workspace opening launches design-time build hosts whose allocations are excluded here; complete-command captures account for those child processes separately.",
            ValidationScope = "Project paths, source counts, compiler errors and generated hint names with content hashes must match a fresh production workspace. Generator-only output must match workspace-generated hint names and contents in every included generator project. At the pinned OrchardCore revision only, ArgumentsFromInterceptors.g.cs contains file-local class names produced by Guid.NewGuid(); those declared identifier tokens are renamed by declaration order before hashing. Every other token and all trivia remain unchanged.",
            CoreInformationalVersion = typeof(CallGraph).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            CoreSha256 = await AssemblyHash(typeof(CallGraph).Assembly),
            WorkerSha256 = await AssemblyHash(typeof(WorkspaceAnalysis).Assembly),
            BenchmarkSha256 = await AssemblyHash(typeof(WorkspaceStageBenchmarks).Assembly)
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Workspace stage workload: " + evidence);
    }

    [IterationSetup(Target = nameof(OpenWorkspace))]
    public void ResetWorkspace() => measuredWorkspace = null;

    [Benchmark]
    public async Task<int> OpenWorkspace()
    {
        measuredWorkspace = await WorkspaceAnalysis.LoadAsync(request);
        return measuredWorkspace.IncludedProjects(false).Count;
    }

    [IterationCleanup(Target = nameof(OpenWorkspace))]
    public async Task CheckWorkspace()
    {
        using var loaded = measuredWorkspace ?? throw new InvalidOperationException("No measured workspace.");
        measuredWorkspace = null;
        var included = loaded.IncludedProjects(false);
        await CheckCompilations(included, await CompileAll(included));
    }

    [IterationSetup(Target = nameof(CompileWithGenerators))]
    public async Task ResetCompilation()
    {
        measuredWorkspace = await WorkspaceAnalysis.LoadAsync(request);
        projects = measuredWorkspace.IncludedProjects(false);
        measuredCompilations = null;
    }

    [Benchmark]
    public async Task<int> CompileWithGenerators()
    {
        measuredCompilations = await CompileAll(projects);
        return measuredCompilations.Sum(compilation => compilation.SyntaxTrees.Length);
    }

    [IterationCleanup(Target = nameof(CompileWithGenerators))]
    public async Task CheckCompilation()
    {
        try
        {
            await CheckCompilations(projects, measuredCompilations ?? throw new InvalidOperationException("No measured compilation."));
        }
        finally
        {
            measuredWorkspace?.Dispose();
            measuredWorkspace = null;
            measuredCompilations = null;
            projects = [];
        }
    }

    [IterationSetup(Target = nameof(RunGenerators))]
    public void ResetGenerators()
    {
        drivers = inputs.Select(input => (GeneratorDriver)CSharpGeneratorDriver.Create(input.Generators, input.AdditionalTexts,
            input.ParseOptions, input.OptionsProvider)).ToArray();
        generatorCompilations = inputs.Select(input => CSharpCompilation.Create(input.Compilation.AssemblyName,
            input.Compilation.SyntaxTrees, input.Compilation.References, input.Compilation.Options)).ToArray();
        measuredGenerators = null;
    }

    [Benchmark]
    public int RunGenerators()
    {
        measuredGenerators = new GeneratorDriverRunResult[inputs.Length];
        for (var index = 0; index < inputs.Length; index++)
            measuredGenerators[index] = drivers[index].RunGenerators(generatorCompilations[index]).GetRunResult();
        return measuredGenerators.Sum(result => result.GeneratedTrees.Length);
    }

    [IterationCleanup(Target = nameof(RunGenerators))]
    public void CheckGenerators()
    {
        var results = measuredGenerators ?? throw new InvalidOperationException("No measured generators.");
        for (var index = 0; index < inputs.Length; index++)
        {
            var result = results[index];
            var actual = result.Results.SelectMany(generator => generator.GeneratedSources)
                .Select(source => Fingerprint(source.HintName, source.SourceText)).Order(StringComparer.Ordinal).ToArray();
            if (result.Results.Any(generator => generator.Exception is not null)
                || result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                || !actual.SequenceEqual(inputs[index].Expected, StringComparer.Ordinal))
            {
                var evidence = Path.Combine(RealWorldCaseStore.FindRoot(), "artifacts", "benchmark-workspace-failures", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "generation.json"), JsonSerializer.Serialize(new
                {
                    inputs[index].Name,
                    Expected = inputs[index].Sources,
                    Actual = result.Results.SelectMany(generator => generator.GeneratedSources)
                        .Select(source => new GeneratedSource(source.HintName, source.SourceText.ToString())).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true }));
                throw new InvalidOperationException($"Generator output differs from the production workspace for {inputs[index].Name}: "
                    + JsonSerializer.Serialize(new
                    {
                        Missing = inputs[index].Expected.Except(actual).ToArray(),
                        Unexpected = actual.Except(inputs[index].Expected).ToArray(),
                        Diagnostics = result.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray(),
                        Exceptions = result.Results.Where(generator => generator.Exception is not null).Select(generator => generator.Exception!.ToString()).ToArray(),
                        Evidence = evidence
                    }));
            }
        }
        measuredGenerators = null;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        measuredWorkspace?.Dispose();
        if (scratch.Length > 0 && Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }

    private async Task CheckCompilations(IReadOnlyList<Project> included, CSharpCompilation[] compilations)
    {
        var actual = new List<ProjectSnapshot>();
        for (var index = 0; index < included.Count; index++) actual.Add(await Capture(included[index], compilations[index]));
        if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(expected))
            throw new InvalidOperationException("Measured workspace compilations differ from the preflight workspace.");
    }

    private async Task<ProjectSnapshot> Capture(Project project, CSharpCompilation compilation)
    {
        var generated = new List<string>();
        foreach (var document in await project.GetSourceGeneratedDocumentsAsync())
            generated.Add(Fingerprint(document.Name, await document.GetTextAsync()));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => MSBuildAnalysisProvider.CleanMessage(diagnostic.ToString(), request.Root)).Order(StringComparer.Ordinal).ToArray();
        return new ProjectSnapshot(Path.GetRelativePath(request.Root, project.FilePath!).Replace('\\', '/'), project.Name,
            compilation.SyntaxTrees.Length, generated.Order(StringComparer.Ordinal).ToArray(), errors);
    }

    private static async Task<CSharpCompilation> Compile(Project project) => await project.GetCompilationAsync() as CSharpCompilation
        ?? throw new InvalidOperationException($"No C# compilation for {project.Name}.");

    private static async Task<CSharpCompilation[]> CompileAll(IReadOnlyList<Project> included)
    {
        var compilations = new List<CSharpCompilation>();
        foreach (var project in included) compilations.Add(await Compile(project));
        return compilations.ToArray();
    }

    private string Fingerprint(string name, SourceText text)
    {
        var content = text.ToString();
        if (randomInterceptorNames && name == "ArgumentsFromInterceptors.g.cs")
        {
            var syntax = CSharpSyntaxTree.ParseText(text).GetRoot();
            var names = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(declaration => declaration.Modifiers.Any(SyntaxKind.FileKeyword) && declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && declaration.Identifier.ValueText.StartsWith("Interceptor_", StringComparison.Ordinal)
                    && declaration.Identifier.ValueText.Length == "Interceptor_".Length + 32
                    && declaration.Identifier.ValueText["Interceptor_".Length..].All(char.IsAsciiHexDigit)
                    && declaration.Members.OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "InterceptFrom"))
                .Select((declaration, index) => (Name: declaration.Identifier.ValueText, Replacement: "Interceptor_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToDictionary(item => item.Name, item => item.Replacement, StringComparer.Ordinal);
            content = syntax.ReplaceTokens(syntax.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken) && names.ContainsKey(token.ValueText)),
                (token, _) => SyntaxFactory.Identifier(token.LeadingTrivia, names[token.ValueText], token.TrailingTrivia)).ToFullString();
        }
        return name + ":" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
    }

    private static async Task<string> AssemblyHash(Assembly assembly) => Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location)));

    private sealed record ProjectSnapshot(string Path, string Name, int SyntaxTrees, string[] Generated, string[] Errors);

    private sealed record GeneratorInput(string Name, CSharpCompilation Compilation, ISourceGenerator[] Generators, AdditionalText[] AdditionalTexts,
        CSharpParseOptions ParseOptions, AnalyzerConfigOptionsProvider OptionsProvider, string[] Expected, GeneratedSource[] Sources);

    private sealed record GeneratedSource(string Name, string Content);

    private sealed class FrozenAdditionalText(string path, SourceText text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => text;
    }
}
