using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class ReachabilityBenchmarks
{
    private CallGraph graph = null!;
    private HashSet<string> changed = null!;
    private readonly HashSet<string> unchanged = new(StringComparer.Ordinal);
    private string[] roots = null!;
    private readonly DiffOptions options = new() { MaxDepth = 1 };

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-breadcrumbs", "aspnetcore-header-recovery")]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = Case switch
        {
            "orchardcore-breadcrumbs" => new RealWorldCase(Case, "https://github.com/OrchardCMS/OrchardCore.git", "orchardcore", "BSD-3-Clause", "LICENSE",
                "cd7d8430905d2f2ab3ba3aef49577d73647ccfbf", "4910d1722d33f1b06d9ddb1cf8f6de2aa871dc8a", "Pinned expansion workload", [], [], null,
                BeforeLicenseBlob: "183936c000fa2cc5969ba724afc1bdc5bfae5080", AfterLicenseBlob: "183936c000fa2cc5969ba724afc1bdc5bfae5080"),
            "aspnetcore-header-recovery" => new RealWorldCase(Case, "https://github.com/dotnet/aspnetcore.git", "aspnetcore", "MIT", "LICENSE.txt",
                "45317bee954f2eb1e4f892c63166dec822caedae", "66e8a029d94b5ce2af03c2c8f8023aba1f39a093", "Pinned expansion workload", [], [], null,
                BeforeLicenseBlob: "984713a49622a96da110443c15477613bc12656b", AfterLicenseBlob: "984713a49622a96da110443c15477613bc12656b"),
            _ => RealWorldCaseStore.ReadManifest().Single(e => e.Id == Case)
        };
        var repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        var before = await repository.ReadSnapshotAsync(entry.Before);
        var after = await repository.ReadSnapshotAsync(entry.After);
        var provider = new SourceOnlyAnalysisProvider();
        var oldGraph = await provider.AnalyzeAsync(before, new AnalysisOptions());
        graph = await provider.AnalyzeAsync(after, new AnalysisOptions());
        changed = ChangeDetector.FindChanges(oldGraph, graph);
        roots = EntrySelector.Select(oldGraph, graph, changed, options).Where(graph.Members.ContainsKey).Take(10).ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("The expansion workload has no roots.");
        Console.WriteLine("Expansion workload: " + JsonSerializer.Serialize(new
        {
            entry.Id,
            entry.Before,
            entry.After,
            Inputs = after.Files.Count,
            Members = graph.Members.Count,
            Changed = changed.Count,
            options.MaxDepth,
            Roots = roots,
            ChangedTreesSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ExpandChanged()))),
            UnchangedTreesSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ExpandUnchanged())))
        }));
    }

    [Benchmark]
    public CallTree[] ExpandChanged() => roots.Select(new TreeExpander(graph, changed, options).Expand).ToArray();

    [Benchmark]
    public CallTree[] ExpandUnchanged() => roots.Select(new TreeExpander(graph, unchanged, options).Expand).ToArray();
}
