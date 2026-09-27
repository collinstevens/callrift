using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Callrift.Cli;
using Callrift.Core;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[EvaluateOverhead(false)]
public class EndToEndBenchmarks
{
    private string repository = "";
    private string evidence = "";
    private string capture = "";
    private string[] arguments = [];
    private string expectedOutput = "";
    private string expectedError = "";
    private string output = "";
    private string error = "";
    private string? previousHooks;
    private string? previousCapture;
    private int iteration;

    [Params("serilog-alignment-guard", "polly-secondary-action", "orchardcore-esmodule-localization")]
    public string Case { get; set; } = "";

    [Params("source", "msbuild")]
    public string Mode { get; set; } = "";

    [Params("warm", "fresh")]
    public string ProcessMode { get; set; } = "";

    [Params("diff", "tree", "reach")]
    public string Command { get; set; } = "";

    [Params(false, true)]
    public bool IncludeTests { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(item => item.Id == Case);
        repository = await RealWorldCaseStore.PrepareAsync(entry);
        var core = typeof(CallGraph).Assembly.Location;
        var workerCore = Path.Combine(Path.GetDirectoryName(typeof(CommandRunner).Assembly.Location)!, "msbuild", "Callrift.Core.dll");
        var coreHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(core)));
        var workerHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(workerCore)));
        if (coreHash != workerHash) throw new InvalidOperationException("Benchmark parent and worker Core assemblies differ.");
        var (selector, target, project, framework) = Case switch
        {
            "serilog-alignment-guard" => ("MessageTemplateParser.ParsePropertyToken", "new TextToken", "src/Serilog/Serilog.csproj", "net10.0"),
            "polly-secondary-action" => ("TaskExecution<T>.InitializeAsync", "TaskExecution<T>.TryCreateSecondaryActionAsync", "src/Polly.Extensions/Polly.Extensions.csproj", "net8.0"),
            "orchardcore-esmodule-localization" => ("LocalizationOrchardHelperExtensions.GetJSLocalizations", "JSLocalizerExtensions.GetMergedLocalizations", "src/OrchardCore.Cms.Web/OrchardCore.Cms.Web.csproj", "net10.0"),
            _ => throw new InvalidOperationException("Unknown benchmark workload.")
        };
        var command = new List<string> { Command, Command == "diff" ? entry.Before : entry.After };
        if (Command == "diff") command.Add(entry.After);
        if (Command != "diff" || Case == "serilog-alignment-guard") command.AddRange(["--entry", selector]);
        if (Command == "reach") command.AddRange(["--to", target]);
        if (IncludeTests) command.Add("--tests");
        command.AddRange(["--depth", Command == "diff" ? "1" : "4", "--format", "json", "--color", "never"]);
        if (Mode == "msbuild") command.AddRange(["--project", project, "--framework", framework]);
        arguments = command.ToArray();
        await InvokeWarm();
        using (var document = JsonDocument.Parse(output))
        {
            var root = document.RootElement;
            if (root.GetProperty("analysis").GetProperty("mode").GetString() != Mode
                || root.GetProperty(Command == "reach" ? "paths" : "trees").GetArrayLength() == 0)
                throw new InvalidOperationException("The benchmark must analyze a nonempty result in its selected mode.");
        }
        expectedOutput = Hash(output);
        expectedError = Hash(error);
        if (Mode == "msbuild") arguments = [.. arguments, "--no-restore"];
        var rootDirectory = RealWorldCaseStore.FindRoot();
        evidence = Path.Combine(rootDirectory, "artifacts", "benchmark-processes", $"{Case}-{Mode}-{ProcessMode}-{Command}-tests-{IncludeTests}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidence);
        var git = await GitRepository.OpenAsync(repository);
        var before = await git.ListEntriesAsync(entry.Before);
        var after = await git.ListEntriesAsync(entry.After);
        int? sourceFiles = Mode == "source"
            ? new SourceOnlyAnalysisProvider().Parse(await git.ReadSnapshotAsync(entry.After), new AnalysisOptions(IncludeTests)).Length : null;
        var workload = new
        {
            Case,
            Mode,
            ProcessMode,
            Command,
            IncludeTests,
            entry.Repository,
            entry.Before,
            entry.After,
            Arguments = arguments,
            BeforeCSharpFiles = before.Count(item => item.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)),
            AfterCSharpFiles = after.Count(item => item.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)),
            ParsedSourceFiles = sourceFiles,
            SelectedProject = Mode == "msbuild" ? project : null,
            CacheCondition = "Prewarmed Git blobs, filesystem and restored workspace; no restore in measured invocations.",
            ProcessCondition = ProcessMode == "warm" ? "Warm CLI host; each restored analysis still launches a fresh worker." : "Fresh CLI and worker processes.",
            AllocationScope = "MemoryDiagnoser measures the benchmark process. Startup-hook exports measure managed allocations at CLI, Callrift worker and Roslyn build-host exit, including hook overhead. Other subprocess and native allocations are excluded.",
            RepositoryHeadAtSetup = (await GitRepository.RunAsync(rootDirectory, ["rev-parse", "HEAD"])).Trim(),
            RepositoryModifiedAtSetup = (await GitRepository.RunAsync(rootDirectory, ["status", "--porcelain", "--untracked-files=no"])).Length > 0,
            CoreInformationalVersion = typeof(CallGraph).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            BenchmarkSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(EndToEndBenchmarks).Assembly.Location))),
            HookSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(StartupHook).Assembly.Location))),
            CoreSha256 = coreHash,
            WorkerCoreSha256 = workerHash,
            OutputSha256 = expectedOutput,
            ErrorSha256 = expectedError
        };
        await File.WriteAllTextAsync(Path.Combine(evidence, "workload.json"), JsonSerializer.Serialize(workload, new JsonSerializerOptions { WriteIndented = true }));
        previousHooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
        previousCapture = Environment.GetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE");
        var hook = typeof(StartupHook).Assembly.Location;
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", string.IsNullOrEmpty(previousHooks) ? hook : previousHooks + Path.PathSeparator + hook);
        Console.WriteLine("Command workload: " + evidence);
    }

    [IterationSetup]
    public void BeginIteration()
    {
        capture = Path.Combine(evidence, (++iteration).ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(capture);
        Environment.SetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE", capture);
        output = "";
        error = "";
    }

    [Benchmark]
    public Task<int> Execute() => ProcessMode == "warm" ? InvokeWarm() : InvokeFresh();

    [IterationCleanup]
    public void EndIteration()
    {
        if (Hash(output) != expectedOutput || Hash(error) != expectedError)
            throw new InvalidOperationException("Benchmark output differs from its validated preflight.");
        var processes = Directory.EnumerateFiles(capture, "process-*.json").Select(path =>
            JsonSerializer.Deserialize<ProcessMeasurement>(File.ReadAllText(path))!).ToArray();
        var workers = processes.Where(process => process.Assembly == "Callrift.MSBuild").ToArray();
        var cli = processes.Where(process => process.Assembly == "callrift").ToArray();
        var buildHosts = processes.Where(process => process.Assembly == "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost").ToArray();
        var expectedWorkers = Mode == "msbuild" ? Command == "diff" ? 2 : 1 : 0;
        if (workers.Length != expectedWorkers || buildHosts.Length < expectedWorkers || cli.Length != (ProcessMode == "fresh" ? 1 : 0)
            || processes.Any(process => process.ExitCode != 0 || process.ManagedAllocatedBytes <= 0))
            throw new InvalidOperationException("Missing or invalid child-process allocation measurements.");
        var summary = new
        {
            Iteration = iteration,
            WorkerProcesses = workers.Length,
            WorkerManagedAllocatedBytes = workers.Sum(process => process.ManagedAllocatedBytes),
            BuildHostProcesses = buildHosts.Length,
            BuildHostManagedAllocatedBytes = buildHosts.Sum(process => process.ManagedAllocatedBytes),
            CliManagedAllocatedBytes = cli.Sum(process => process.ManagedAllocatedBytes),
            OutputSha256 = Hash(output),
            ErrorSha256 = Hash(error)
        };
        File.WriteAllText(Path.Combine(capture, "summary.json"), JsonSerializer.Serialize(summary));
        Console.WriteLine("Process allocations: " + capture);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", previousHooks);
        Environment.SetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE", previousCapture);
    }

    private async Task<int> InvokeWarm()
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var code = await CommandRunner.RunAsync(arguments, repository, stdout, stderr, timeout.Token);
        output = stdout.ToString();
        error = stderr.ToString();
        if (code != 0) throw new InvalidOperationException(error);
        return output.Length + error.Length;
    }

    private async Task<int> InvokeFresh()
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(CommandRunner).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start benchmark CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        output = (await stdout).ReplaceLineEndings("\n");
        error = (await stderr).ReplaceLineEndings("\n");
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output.Length + error.Length;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record ProcessMeasurement(string Assembly, long ManagedAllocatedBytes, int ExitCode);
}
