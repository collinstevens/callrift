using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[EvaluateOverhead(false)]
public class PairAnalysisBenchmarks
{
    private readonly SourceOnlyAnalysisProvider provider = new();
    private SourceSnapshot before = null!;
    private SourceSnapshot after = null!;
    private AnalysisOptions analysis = null!;
    private DiffOptions options = null!;
    private JsonNode[] expected = [];
    private CallGraph[]? measured;
    private string expectedDiff = "";
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 1024 };

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-esmodule-localization")]
    public string Case { get; set; } = "";

    [Params(false, true)]
    public bool IncludeTests { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(item => item.Id == Case);
        var repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        before = await repository.ReadSnapshotAsync(entry.Before);
        after = await repository.ReadSnapshotAsync(entry.After);
        analysis = new AnalysisOptions(IncludeTests);
        options = new DiffOptions
        {
            IncludeTests = IncludeTests,
            MaxDepth = 1,
            Entries = Case == "serilog-alignment-guard" ? ["MessageTemplateParser.ParsePropertyToken"] : []
        };
        var baseline = await AnalyzeSeparately();
        expected = baseline.Select(GraphSnapshot).ToArray();
        var diff = CallriftService.Compare(baseline[0], baseline[1], options);
        if (diff.Trees.Count == 0) throw new InvalidOperationException("The pair-analysis comparison has no affected roots.");
        expectedDiff = JsonRenderer.Render(diff);
        await ReuseParsedTrees();
        CheckGraphs();
        var beforeSyntax = provider.Parse(before, analysis);
        var afterSyntax = provider.Parse(after, analysis);
        var previous = beforeSyntax.ToDictionary(tree => tree.FilePath, tree => tree.GetText().ToString(), StringComparer.Ordinal);
        var evidence = Path.Combine(RealWorldCaseStore.FindRoot(), "artifacts", "benchmark-pair-analysis", $"{Case}-tests-{IncludeTests}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "workload.json"), JsonSerializer.Serialize(new
        {
            Case,
            IncludeTests,
            entry.Repository,
            entry.Before,
            entry.After,
            ParsedBeforeFiles = beforeSyntax.Length,
            ParsedAfterFiles = afterSyntax.Length,
            SharedFiles = afterSyntax.Count(tree => previous.TryGetValue(tree.FilePath, out var content) && content == tree.GetText().ToString()),
            CacheCondition = "Preloaded snapshots, warm framework references and process. Each invocation creates new compilations and graphs. Both methods analyze the pair concurrently. ReuseParsedTrees shares identical path/content syntax trees only within that invocation; AnalyzeSeparately parses each snapshot independently.",
            StageScope = "Source-only pair analysis, including classification, parsing, binding, implementation mapping and diagnostics. Excludes Git reads, context construction, diff comparison, rendering and process startup. Ratios compare this optimization on equivalent workloads and do not represent complete-command speedups.",
            AllocationScope = "MemoryDiagnoser measures all managed allocations in the benchmark process, including analysis tasks. These methods launch no child processes.",
            ValidationScope = "Every measured pair must match the independent-analysis preflight graphs, including all members and diagnostics, and the rendered public comparison. Only diagnostic and dispatch-contract enumeration order is canonicalized for full-graph comparison.",
            CoreInformationalVersion = typeof(CallGraph).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            CoreSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(CallGraph).Assembly.Location))),
            BenchmarkSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(PairAnalysisBenchmarks).Assembly.Location)))
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Pair analysis workload: " + evidence);
    }

    [IterationSetup]
    public void Reset() => measured = null;

    [Benchmark(Baseline = true)]
    public async Task<CallGraph[]> AnalyzeSeparately() => measured = await Task.WhenAll(
        provider.AnalyzeAsync(before, analysis), provider.AnalyzeAsync(after, analysis));

    [Benchmark]
    public async Task<CallGraph[]> ReuseParsedTrees() => measured = await provider.AnalyzePairAsync(before, after, analysis, CancellationToken.None);

    [IterationCleanup]
    public void CheckGraphs()
    {
        var graphs = measured ?? throw new InvalidOperationException("No measured analysis pair.");
        if (graphs.Length != 2 || !JsonNode.DeepEquals(expected[0], GraphSnapshot(graphs[0]))
            || !JsonNode.DeepEquals(expected[1], GraphSnapshot(graphs[1]))
            || JsonRenderer.Render(CallriftService.Compare(graphs[0], graphs[1], options)) != expectedDiff)
            throw new InvalidOperationException("Pair analysis changed the preflight graphs or rendered comparison.");
        measured = null;
    }

    private static JsonNode GraphSnapshot(CallGraph graph) => JsonSerializer.SerializeToNode(graph with
    {
        Diagnostics = graph.Diagnostics.OrderBy(diagnostic => JsonSerializer.Serialize(diagnostic, JsonOptions), StringComparer.Ordinal).ToArray(),
        DispatchContracts = graph.DispatchContracts.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<DispatchContract>)pair.Value.OrderBy(contract => JsonSerializer.Serialize(contract, JsonOptions), StringComparer.Ordinal).ToArray(), StringComparer.Ordinal)
    }, JsonOptions)!;
}
