using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.RealWorldCases;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class SourceStageBenchmarks
{
    private readonly SourceOnlyAnalysisProvider provider = new();
    private GitRepository repository = null!;
    private RealWorldCase entry = null!;
    private IReadOnlyList<GitEntry> entries = null!;
    private SourceSnapshot after = null!;
    private SyntaxTree[] trees = null!;
    private CSharpCompilation compilation = null!;
    private SourceOnlyAnalysisProvider.CollectedCompilation collected = null!;
    private SourceOnlyAnalysisProvider.CollectedCompilation? measuredCollection;
    private CallGraph? measuredGraph;
    private CallGraph originalBefore = null!;
    private CallGraph originalAfter = null!;
    private CallGraph beforeGraph = null!;
    private CallGraph afterGraph = null!;
    private HashSet<string> changed = null!;
    private IReadOnlyList<string> roots = null!;
    private CallTree[] beforeTrees = null!;
    private CallTree[] afterTrees = null!;
    private DiffResult diff = null!;
    private DiffOptions options = null!;
    private JsonNode expectedGraph = null!;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 1024 };

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-esmodule-localization")]
    public string Case { get; set; } = "";

    [Params(false, true)]
    public bool IncludeTests { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        entry = RealWorldCaseStore.ReadManifest().Single(item => item.Id == Case);
        repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        entries = await repository.ListEntriesAsync(entry.After);
        var before = await repository.ReadSnapshotAsync(entry.Before);
        after = await repository.ReadSnapshotAsync(entry.After);
        var analysis = new AnalysisOptions(IncludeTests);
        trees = provider.Parse(after, analysis);
        var beforeSyntax = provider.Parse(before, analysis);
        compilation = provider.CreateCompilation(trees);
        collected = SourceOnlyAnalysisProvider.CollectCompilation(compilation, trees);
        originalAfter = SourceOnlyAnalysisProvider.BuildGraph(collected);
        originalBefore = SourceOnlyAnalysisProvider.AnalyzeCompilation(provider.CreateCompilation(beforeSyntax), beforeSyntax);
        beforeGraph = ContextGraph.Create(originalBefore);
        afterGraph = ContextGraph.Create(originalAfter);
        options = new DiffOptions
        {
            IncludeTests = IncludeTests,
            MaxDepth = 1,
            Entries = Case == "serilog-alignment-guard" ? ["MessageTemplateParser.ParsePropertyToken"] : []
        };
        changed = ChangeDetector.FindChanges(beforeGraph, afterGraph);
        roots = EntrySelector.Select(beforeGraph, afterGraph, changed, options);
        (beforeTrees, afterTrees) = ExpandPair();
        diff = CallriftService.Compare(originalBefore, originalAfter, options);
        var aligned = TreeDiffer.Compare(beforeTrees, afterTrees).Where(tree => tree.HasChanges).ToArray();
        if (diff.Trees.Count == 0 || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(aligned, JsonOptions), JsonSerializer.SerializeToNode(diff.Trees, JsonOptions)))
            throw new InvalidOperationException("Stage reconstruction differs from the complete graph comparison.");
        expectedGraph = GraphSnapshot(originalAfter);
        var evidence = Path.Combine(RealWorldCaseStore.FindRoot(), "artifacts", "benchmark-stages", $"{Case}-tests-{IncludeTests}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "workload.json"), JsonSerializer.Serialize(new
        {
            Case,
            IncludeTests,
            entry.Repository,
            entry.Before,
            entry.After,
            ParsedBeforeFiles = beforeSyntax.Length,
            ParsedAfterFiles = trees.Length,
            options.MaxDepth,
            options.Entries,
            CacheCondition = "Prewarmed Git blobs and filesystem. Binding uses a fresh compilation with parsed trees and cached framework references. Dispatch uses a fresh compilation and freshly collected members and symbols prepared outside timing for each iteration; no previous dispatch mapping is reused.",
            StageScope = "Git, parsing, compilation, binding and dispatch use the after snapshot. Context, equivalence, selection, expansion and alignment use both snapshots. No pair parsing reuse is measured. These isolated costs must not be summed as concurrent complete-command latency.",
            DiagnosticScope = "Compilation diagnostics; project-classifier diagnostics are outside these isolated stages.",
            CoreInformationalVersion = typeof(CallGraph).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            CoreSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(CallGraph).Assembly.Location))),
            BenchmarkSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(SourceStageBenchmarks).Assembly.Location)))
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Stage workload: " + evidence);
    }

    [Benchmark]
    public Task<IReadOnlyList<GitEntry>> ListPaths() => repository.ListEntriesAsync(entry.After);

    [Benchmark]
    public Task<IReadOnlyList<SourceFile>> ReadBlobs() => repository.ReadBlobsAsync(entries);

    [Benchmark]
    public SyntaxTree[] ParseFiles() => provider.Parse(after, new AnalysisOptions(IncludeTests));

    [Benchmark]
    public IReadOnlyList<MetadataReference> LoadReferences() => SourceOnlyAnalysisProvider.LoadReferences();

    [Benchmark]
    public CSharpCompilation CreateCompilation() => provider.CreateCompilation(trees);

    [IterationSetup(Target = nameof(BindAndCollect))]
    public void ResetCompilation()
    {
        compilation = provider.CreateCompilation(trees);
        measuredCollection = null;
    }

    [Benchmark]
    public object BindAndCollect() => measuredCollection = SourceOnlyAnalysisProvider.CollectCompilation(compilation, trees);

    [IterationCleanup(Target = nameof(BindAndCollect))]
    public void CheckCollection()
    {
        CheckGraph(SourceOnlyAnalysisProvider.BuildGraph(measuredCollection ?? throw new InvalidOperationException("No measured collection.")));
        measuredCollection = null;
    }

    [IterationSetup(Target = nameof(BuildDispatchAndGraph))]
    public void ResetGraph()
    {
        collected = SourceOnlyAnalysisProvider.CollectCompilation(provider.CreateCompilation(trees), trees);
        measuredGraph = null;
    }

    [Benchmark]
    public CallGraph BuildDispatchAndGraph() => measuredGraph = SourceOnlyAnalysisProvider.BuildGraph(collected);

    [IterationCleanup(Target = nameof(BuildDispatchAndGraph))]
    public void CheckDispatch()
    {
        CheckGraph(measuredGraph ?? throw new InvalidOperationException("No measured graph."));
        measuredGraph = null;
    }

    [Benchmark]
    public (CallGraph Before, CallGraph After) BuildContexts() => (ContextGraph.Create(originalBefore), ContextGraph.Create(originalAfter));

    [Benchmark]
    public HashSet<string> Equivalence() => ChangeDetector.FindChanges(beforeGraph, afterGraph);

    [Benchmark]
    public IReadOnlyList<string> SelectRoots() => EntrySelector.Select(beforeGraph, afterGraph, changed, options);

    [Benchmark]
    public (CallTree[] Before, CallTree[] After) ExpandPair()
    {
        var oldExpander = new TreeExpander(beforeGraph, changed, options);
        var newExpander = new TreeExpander(afterGraph, changed, options);
        return (
            roots.Where(beforeGraph.Members.ContainsKey).Select(oldExpander.Expand).OrderBy(tree => tree.MatchName, StringComparer.Ordinal).ThenBy(tree => tree.Key, StringComparer.Ordinal).ToArray(),
            roots.Where(afterGraph.Members.ContainsKey).Select(newExpander.Expand).OrderBy(tree => tree.MatchName, StringComparer.Ordinal).ThenBy(tree => tree.Key, StringComparer.Ordinal).ToArray());
    }

    [Benchmark]
    public IReadOnlyList<DiffNode> AlignTrees() => TreeDiffer.Compare(beforeTrees, afterTrees);

    [Benchmark]
    public string RenderText() => DiffRenderer.Render(diff, options);

    [Benchmark]
    public string RenderMarkdown() => DiffRenderer.Render(diff, options, markdown: true);

    [Benchmark]
    public string RenderJson() => JsonRenderer.Render(diff);

    private void CheckGraph(CallGraph graph)
    {
        if (!JsonNode.DeepEquals(expectedGraph, GraphSnapshot(graph))
            || JsonRenderer.Render(CallriftService.Compare(originalBefore, graph, options)) != JsonRenderer.Render(diff))
            throw new InvalidOperationException("Measured analysis differs from its preflight graph or comparison.");
    }

    private static JsonNode GraphSnapshot(CallGraph graph) => JsonSerializer.SerializeToNode(graph with
    {
        Diagnostics = graph.Diagnostics.OrderBy(diagnostic => JsonSerializer.Serialize(diagnostic, JsonOptions), StringComparer.Ordinal).ToArray(),
        DispatchContracts = graph.DispatchContracts.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<DispatchContract>)pair.Value.OrderBy(contract => JsonSerializer.Serialize(contract, JsonOptions), StringComparer.Ordinal).ToArray(), StringComparer.Ordinal)
    }, JsonOptions)!;
}
