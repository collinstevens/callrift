using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.MSBuild;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[EvaluateOverhead(false)]
public class RestoreBenchmarks
{
    private SourceSnapshot snapshot = null!;
    private string scratch = "";
    private string root = "";
    private string target = "";
    private string evidence = "";
    private string capture = "";
    private string? previousHooks;
    private string? previousCapture;
    private string? previousCaptureSdk;
    private Dictionary<string, string> expectedAssets = null!;
    private int iteration;

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-esmodule-localization")]
    public string Case { get; set; } = "";

    [Params(false, true)]
    public bool ExistingAssets { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        previousHooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
        previousCapture = Environment.GetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE");
        previousCaptureSdk = Environment.GetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE_SDK");
        var entry = RealWorldCaseStore.ReadManifest().Single(item => item.Id == Case);
        var repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        snapshot = await repository.ReadSnapshotAsync(entry.After, allFiles: true);
        var project = Case switch
        {
            "serilog-alignment-guard" => "src/Serilog/Serilog.csproj",
            "polly-secondary-action" => "src/Polly.Extensions/Polly.Extensions.csproj",
            "orchardcore-esmodule-localization" => "src/OrchardCore.Cms.Web/OrchardCore.Cms.Web.csproj",
            _ => throw new InvalidOperationException("Unknown restore workload.")
        };
        scratch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Callrift", "benchmark-restores", Guid.NewGuid().ToString("N"));
        root = Path.Combine(scratch, "tree");
        target = Path.Combine(root, project);
        await MSBuildAnalysisProvider.MaterializeAsync(snapshot, root);
        await Restore();
        expectedAssets = ReadAssets();
        if (expectedAssets.Count == 0)
            throw new InvalidOperationException("Restore produced no resolved dependency assets.");
        evidence = Path.Combine(RealWorldCaseStore.FindRoot(), "artifacts", "benchmark-restores",
            $"{Case}-existing-{ExistingAssets}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "workload.json"), JsonSerializer.Serialize(new
        {
            Case,
            ExistingAssets,
            entry.Repository,
            entry.Before,
            entry.After,
            Project = project,
            Configuration = "Debug",
            AssetFiles = expectedAssets.Keys.Order(StringComparer.Ordinal).ToArray(),
            CacheCondition = "Global packages and NuGet HTTP caches prewarmed by a setup restore; no package-cache or operating-system cache eviction. Fresh assets use a freshly materialized tree outside timing. Existing assets retain setup or preceding restore outputs.",
            StageScope = "Production dotnet restore invocation for the after snapshot, including fresh SDK process startup and every target framework selected by the project. Excludes materialization, restored-marker writes and workspace loading. Cleanup validates dependency targets and exact asset-file hashes outside timing.",
            AllocationScope = "MemoryDiagnoser measures the benchmark process. Startup-hook captures sum managed allocations from dotnet and MSBuild processes, including hook overhead. Native and other subprocess allocations are excluded.",
            CoreInformationalVersion = typeof(CallGraph).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            CoreSha256 = await AssemblyHash(typeof(CallGraph)),
            WorkerSha256 = await AssemblyHash(typeof(MSBuildAnalysisProvider)),
            BenchmarkSha256 = await AssemblyHash(typeof(RestoreBenchmarks)),
            HookSha256 = await AssemblyHash(typeof(StartupHook))
        }, new JsonSerializerOptions { WriteIndented = true }));
        var hook = typeof(StartupHook).Assembly.Location;
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", string.IsNullOrEmpty(previousHooks) ? hook : previousHooks + Path.PathSeparator + hook);
        Environment.SetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE_SDK", "1");
        Console.WriteLine("Restore workload: " + evidence);
    }

    [IterationSetup]
    public async Task PrepareAssets()
    {
        if (!ExistingAssets)
        {
            Directory.Delete(root, recursive: true);
            await MSBuildAnalysisProvider.MaterializeAsync(snapshot, root);
        }
        capture = Path.Combine(evidence, (++iteration).ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(capture);
        Environment.SetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE", capture);
    }

    [Benchmark]
    public async Task Restore()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await MSBuildAnalysisProvider.RestoreAsync(root, target, "Debug", timeout.Token);
    }

    [IterationCleanup]
    public void ValidateRestore()
    {
        var actual = ReadAssets();
        if (actual.Count != expectedAssets.Count || expectedAssets.Any(pair =>
            !actual.TryGetValue(pair.Key, out var assets) || pair.Value != assets))
            throw new InvalidOperationException("Restored dependency assets differ from the pinned preflight.");
        var processes = Directory.EnumerateFiles(capture, "process-*.json")
            .Select(path => JsonSerializer.Deserialize<ProcessMeasurement>(File.ReadAllText(path))!).ToArray();
        if (processes.Length == 0 || processes.All(process => process.Assembly is not ("dotnet" or "MSBuild"))
            || processes.Any(process => process.ExitCode != 0 || process.ManagedAllocatedBytes <= 0)
            || processes.Select(process => process.ProcessId).Distinct().Count() != processes.Length
            || Directory.EnumerateFiles(capture, "*.tmp").Any())
            throw new InvalidOperationException("Missing or invalid restore-process allocation measurements.");
        File.WriteAllText(Path.Combine(capture, "summary.json"), JsonSerializer.Serialize(new
        {
            Iteration = iteration,
            AssetFiles = actual.Count,
            Processes = processes.Length,
            ManagedAllocatedBytes = processes.Sum(process => process.ManagedAllocatedBytes)
        }));
        Console.WriteLine("Restore allocations: " + capture);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", previousHooks);
        Environment.SetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE", previousCapture);
        Environment.SetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE_SDK", previousCaptureSdk);
        if (scratch.Length > 0 && Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }

    private Dictionary<string, string> ReadAssets()
    {
        var assets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "project.assets.json", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);
            var targets = document.RootElement.GetProperty("targets");
            if (targets.ValueKind != JsonValueKind.Object || !targets.EnumerateObject().Any())
                throw new InvalidOperationException($"Restore produced no resolved dependency targets: {path}");
            assets.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        return assets;
    }

    private static async Task<string> AssemblyHash(Type type) => Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(type.Assembly.Location)));

    private sealed record ProcessMeasurement(int ProcessId, string Assembly, long ManagedAllocatedBytes, int ExitCode);
}
