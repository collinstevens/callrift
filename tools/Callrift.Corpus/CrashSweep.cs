using System.Diagnostics;
using System.Text.Json;
using Callrift.Core;

namespace Callrift.Corpus;

public static class CrashSweep
{
    public static async Task<int> RunAsync(int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentException("Sweep limit must be between 1 and 1000.");
        var failed = false;
        foreach (var entry in CorpusStore.ReadManifest().DistinctBy(e => e.Repository))
        {
            var repository = await CorpusStore.PrepareAsync(entry);
            await GitRepository.RunAsync(repository, ["fetch", "--filter=blob:none", "origin", "HEAD"]);
            var history = await GitRepository.RunAsync(repository, ["log", "FETCH_HEAD", "--no-merges", "--format=%H", "-" + limit, "--", "*.cs"]);
            foreach (var revision in history.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var timer = Stopwatch.StartNew();
                string? failure = null;
                var roots = 0;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                try
                {
                    var parent = (await GitRepository.RunAsync(repository, ["rev-parse", revision + "^"], cancellation.Token)).Trim();
                    var result = await new CallriftService().DiffAsync(new DiffRequest(repository, parent, revision), cancellation.Token);
                    roots = result.Trees.Count;
                    _ = JsonRenderer.Render(result);
                    _ = DiffRenderer.Render(result, new DiffOptions());
                }
                catch (Exception exception)
                {
                    failed = true;
                    failure = exception.GetType().Name + ": " + exception.Message;
                }
                Console.WriteLine(JsonSerializer.Serialize(new { repository = entry.Repository, revision, milliseconds = timer.Elapsed.TotalMilliseconds, roots, failure }));
            }
        }
        return failed ? 1 : 0;
    }
}
