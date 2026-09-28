using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.MSBuild;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[EvaluateOverhead(false)]
public class MaterializationBenchmarks
{
    private SourceSnapshot snapshot = null!;
    private string scratch = "";
    private string root = "";

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-esmodule-localization")]
    public string Case { get; set; } = "";

    [Params(false, true)]
    public bool ExistingDirectory { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(item => item.Id == Case);
        var repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        snapshot = await repository.ReadSnapshotAsync(entry.After, allFiles: true);
        if (snapshot.Files.Count == 0) throw new InvalidOperationException("The materialization workload is empty.");
        scratch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Callrift", "benchmark-materializations", Guid.NewGuid().ToString("N"));
        root = Path.Combine(scratch, "tree");
        await MSBuildAnalysisProvider.MaterializeAsync(snapshot, root);
        await ValidateFiles();
        var evidence = Path.Combine(RealWorldCaseStore.FindRoot(), "artifacts", "benchmark-materializations",
            $"{Case}-existing-{ExistingDirectory}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "workload.json"), JsonSerializer.Serialize(new
        {
            Case,
            ExistingDirectory,
            entry.Repository,
            entry.Before,
            entry.After,
            Files = snapshot.Files.Count,
            Bytes = snapshot.Files.Sum(file => (long)(file.RawBytes?.Length ?? Encoding.UTF8.GetByteCount(file.Content))),
            CacheCondition = "Preloaded Git snapshot and warm filesystem cache. Fresh directories are removed before timing; existing directories contain the same snapshot. No operating-system cache eviction is performed.",
            StageScope = "Production materialization loop for the after snapshot, including directory creation and file writes. Excludes Git reads, cache-key calculation, lock acquisition, restore and workspace loading. Iteration cleanup verifies every file byte outside timing.",
            AllocationScope = "MemoryDiagnoser measures managed allocations in the benchmark process. Materialization launches no child processes.",
            CoreInformationalVersion = typeof(CallGraph).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            CoreSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(CallGraph).Assembly.Location))),
            WorkerSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(MSBuildAnalysisProvider).Assembly.Location))),
            BenchmarkSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(MaterializationBenchmarks).Assembly.Location)))
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Materialization workload: " + evidence);
    }

    [IterationSetup]
    public void PrepareDirectory()
    {
        if (!ExistingDirectory && Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Benchmark]
    public Task Materialize() => MSBuildAnalysisProvider.MaterializeAsync(snapshot, root);

    [IterationCleanup]
    public async Task ValidateFiles()
    {
        if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count() != snapshot.Files.Count)
            throw new InvalidOperationException("Materialized file count differs from the pinned snapshot.");
        foreach (var file in snapshot.Files)
        {
            var actual = await File.ReadAllBytesAsync(Path.GetFullPath(file.Path, root));
            var expected = file.RawBytes ?? Encoding.UTF8.GetBytes(file.Content);
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException($"Materialized bytes differ from the pinned snapshot: {file.Path}");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (scratch.Length > 0 && Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}
