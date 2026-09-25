using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.MSBuild;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class WorkspaceBenchmarks
{
    private GitRepository repository = null!;
    private RealWorldCase entry = null!;
    private SourceSnapshot snapshot = null!;
    private readonly MSBuildAnalysisProvider provider = new(new MSBuildOptions("src/Serilog/Serilog.csproj", "net10.0", NoRestore: true));
    private readonly DiffOptions options = new() { Entries = ["MessageTemplateParser.ParsePropertyToken"] };

    [GlobalSetup]
    public async Task Setup()
    {
        entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == "serilog-alignment-guard");
        repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        snapshot = await repository.ReadSnapshotAsync(entry.After, allFiles: true);
        var restore = new MSBuildAnalysisProvider(new MSBuildOptions("src/Serilog/Serilog.csproj", "net10.0"));
        await restore.AnalyzeAsync(snapshot, new AnalysisOptions());
        await restore.AnalyzeAsync(await repository.ReadSnapshotAsync(entry.Before, allFiles: true), new AnalysisOptions());
    }

    [Benchmark]
    public Task<SourceSnapshot> CaptureProjectFiles() => repository.ReadSnapshotAsync(entry.After, allFiles: true);

    [Benchmark]
    public Task<CallGraph> OpenBindAndCollectWorkspace() => provider.AnalyzeAsync(snapshot, new AnalysisOptions());

    [Benchmark]
    public Task<DiffResult> Diff() => new CallriftService(provider).DiffAsync(new DiffRequest(repository.Root, entry.Before, entry.After) { Options = options });

    [Benchmark]
    public Task<DiffResult> Tree() => new CallQueries(provider).RunAsync(new QueryRequest(repository.Root) { Revision = entry.After, Options = options });

    [Benchmark]
    public Task<DiffResult> Reach() => new CallQueries(provider).RunAsync(new QueryRequest(repository.Root) { Revision = entry.After, Options = options, Target = "new TextToken" });
}
