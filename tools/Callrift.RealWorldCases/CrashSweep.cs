using System.Diagnostics;
using System.Text.Json;
using Callrift.Core;

namespace Callrift.RealWorldCases;

public static class CrashSweep
{
    public static async Task<int> RunAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentException("Sweep limit must be between 1 and 1000.");
        var failed = false;
        foreach (var entry in RealWorldCaseStore.ReadManifest().DistinctBy(e => e.Repository))
        {
            var setup = Stopwatch.StartNew();
            string repository;
            string history;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(5));
                repository = await RealWorldCaseStore.PrepareAsync(entry, deadline.Token);
                await GitRepository.RunAsync(repository, ["fetch", "--filter=blob:none", "origin", "HEAD"], deadline.Token);
                history = await GitRepository.RunAsync(repository, ["log", "FETCH_HEAD", "--no-merges", "--format=%H", "-" + limit, "--", "*.cs"], deadline.Token);
            }
            catch (Exception exception)
            {
                Write(entry.Repository, null, "setup", setup.Elapsed, 0, exception.GetType().Name + ": " + exception.Message);
                if (cancellationToken.IsCancellationRequested) return 1;
                failed = true;
                continue;
            }
            foreach (var revision in history.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var timer = Stopwatch.StartNew();
                string? failure = null;
                var roots = 0;
                try
                {
                    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
                    foreach (var argument in new[] { typeof(CrashSweep).Assembly.Location, "sweep-revision", repository, revision })
                        start.ArgumentList.Add(argument);
                    var result = await SweepProcess.RunAsync(start, TimeSpan.FromMinutes(5), cancellationToken);
                    if (result.TimedOut) failure = result.Error;
                    else if (result.ExitCode != 0) failure = $"Sweep worker exited with code {result.ExitCode}: {result.Error.Trim()}";
                    else
                    {
                        using var document = JsonDocument.Parse(result.Output);
                        roots = document.RootElement.GetProperty("roots").GetInt32();
                    }
                }
                catch (Exception exception)
                {
                    failure = exception.GetType().Name + ": " + exception.Message;
                }
                Write(entry.Repository, revision, "revision", timer.Elapsed, roots, failure);
                failed |= failure is not null;
                if (cancellationToken.IsCancellationRequested) return 1;
            }
        }
        return failed ? 1 : 0;
    }

    public static async Task<int> RunRevisionAsync(string repository, string revision)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var parent = (await GitRepository.RunAsync(repository, ["rev-parse", revision + "^"], deadline.Token)).Trim();
            var result = await new CallriftService().DiffAsync(new DiffRequest(repository, parent, revision), deadline.Token);
            _ = JsonRenderer.Render(result);
            _ = DiffRenderer.Render(result, new DiffOptions());
            _ = DiffRenderer.Render(result, new DiffOptions(), markdown: true);
            Console.WriteLine(JsonSerializer.Serialize(new { roots = result.Trees.Count }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
    }

    private static void Write(string repository, string? revision, string stage, TimeSpan elapsed, int roots, string? failure)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { repository, revision, stage, milliseconds = elapsed.TotalMilliseconds, roots, failure }));
        Console.Out.Flush();
    }
}
