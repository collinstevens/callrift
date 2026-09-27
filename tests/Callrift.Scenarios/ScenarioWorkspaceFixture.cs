using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Callrift.Core;
using Callrift.MSBuild;

namespace Callrift.Scenarios;

internal static class ScenarioWorkspaceFixture
{
    private static readonly SemaphoreSlim Slots = new(4);
    private static readonly Lock Gate = new();
    private static readonly List<FixtureWorker> Workers = [];
    private static readonly List<FixtureWorker> Available = [];

    public static async Task<(CallGraph Before, CallGraph After)> AnalyzeAsync(Scenario scenario, bool includeTests)
    {
        var options = new MSBuildOptions("App.csproj", NoRestore: true);
        var beforeShape = FixtureProjectShape.Create(scenario.Before, options);
        var afterShape = FixtureProjectShape.Create(scenario.After, options);
        if (beforeShape is null || afterShape is null)
            return await WorkspaceFixture.AnalyzeAsync(scenario, includeTests, options);
        if (!await Slots.WaitAsync(TimeSpan.FromMinutes(3))) throw new TimeoutException("No fixture worker became available within three minutes.");
        FixtureWorker worker;
        lock (Gate)
        {
            worker = Available.FirstOrDefault(candidate => candidate.RestoredKey == beforeShape.Key)
                ?? Available.FirstOrDefault() ?? new FixtureWorker();
            if (!Available.Remove(worker)) Workers.Add(worker);
        }
        var successful = false;
        try
        {
            var before = await worker.AnalyzeAsync(scenario.Before, beforeShape, options, includeTests);
            var after = await worker.AnalyzeAsync(scenario.After, afterShape, options, includeTests);
            successful = true;
            return (before, after);
        }
        finally
        {
            try
            {
                lock (Gate)
                {
                    if (successful) Available.Add(worker);
                    else Workers.Remove(worker);
                }
                if (!successful) await worker.DisposeAsync();
            }
            finally
            {
                Slots.Release();
            }
        }
    }

    public static async Task DisposeAsync()
    {
        FixtureWorker[] workers;
        lock (Gate)
        {
            workers = Workers.ToArray();
            Workers.Clear();
            Available.Clear();
        }
        await Task.WhenAll(workers.Select(worker => worker.DisposeAsync().AsTask()));
    }

    private sealed class FixtureWorker : IAsyncDisposable
    {
        private readonly string directory = FixtureDirectory.CreatePath("callrift-fixture-worker-");
        private Process? process;
        private Task<string>? stderr;
        private string? restoredRoot;

        public string? RestoredKey { get; private set; }

        public async Task<CallGraph> AnalyzeAsync(IReadOnlyDictionary<string, string> files, FixtureProjectShape shape,
            MSBuildOptions options, bool includeTests)
        {
            var reuse = shape.ReuseRoot && RestoredKey == shape.Key;
            var root = reuse ? restoredRoot! : Path.Combine(directory, Guid.NewGuid().ToString("N"));
            foreach (var file in files)
            {
                if (reuse && !file.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                var path = Path.GetFullPath(file.Key, root);
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new ArgumentException("Fixture path escapes the workspace.", nameof(files));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Value.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
            }
            if (!reuse)
            {
                await WorkspaceFixture.RunAsync(root, ["restore", options.Target, "--nologo", "-p:Configuration=" + options.Configuration]);
                if (shape.ReuseRoot)
                {
                    RestoredKey = shape.Key;
                    restoredRoot = root;
                }
            }
            if (process is null)
            {
                var assembly = Path.Combine(AppContext.BaseDirectory, "fixture-worker", "Callrift.FixtureWorker.dll");
                var start = WorkspaceFixture.CreateStartInfo(directory, [assembly, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]);
                start.RedirectStandardInput = true;
                start.Environment["UseSharedCompilation"] = "false";
                process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the fixture worker.");
                stderr = process.StandardError.ReadToEndAsync();
            }
            var requestPath = Path.Combine(directory, "request-" + Guid.NewGuid().ToString("N") + ".json");
            var resultPath = requestPath + ".graph.json";
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new WorkspaceRequest(root, options, includeTests, resultPath)), timeout.Token);
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(requestPath).AsMemory(), timeout.Token);
                await process.StandardInput.FlushAsync(timeout.Token);
                var response = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (response is null)
                {
                    await StopAsync();
                    throw new InvalidOperationException($"Fixture worker exited without a response.\nstderr:\n{await stderr!.WaitAsync(TimeSpan.FromSeconds(5))}");
                }
                string? failure;
                try { failure = JsonSerializer.Deserialize<string>(response); }
                catch (JsonException error) { throw new InvalidOperationException("Invalid fixture response:\n" + response, error); }
                if (failure is not null) throw new InvalidOperationException("Fixture analysis failed:\n" + failure);
                return JsonSerializer.Deserialize<CallGraph>(await File.ReadAllTextAsync(resultPath, timeout.Token), new JsonSerializerOptions { MaxDepth = 1024 })
                    ?? throw new InvalidOperationException("Fixture worker returned no graph.");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                await StopAsync();
                throw new TimeoutException($"Fixture analysis exceeded three minutes.\nstderr:\n{await stderr!.WaitAsync(TimeSpan.FromSeconds(5))}");
            }
            finally
            {
                File.Delete(requestPath);
                File.Delete(resultPath);
            }
        }

        private async Task StopAsync()
        {
            if (process is null) return;
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync();
                if (stderr is not null) await stderr.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                process?.Dispose();
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
