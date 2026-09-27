using Callrift.Cli;
using VerifyXunit;
using Xunit;

[assembly: CollectionBehavior(MaxParallelThreads = 2)]

namespace Callrift.RealWorldCases;

public sealed class RealWorldCaseTests
{
    [Fact]
    public async Task RestoredSerilog()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == "serilog-alignment-guard");
        var repository = await RealWorldCaseStore.PrepareAsync(entry, timeout.Token);
        var outputs = new List<string>();
        foreach (var format in new[] { "text", "md", "json" })
        {
            using var stdout = new StringWriter { NewLine = "\n" };
            using var stderr = new StringWriter { NewLine = "\n" };
            string[] restore = format == "text" ? [] : ["--no-restore"];
            var code = await CommandRunner.RunAsync(["diff", entry.Before, entry.After, "--format", format,
                "--project", "src/Serilog/Serilog.csproj", "--framework", "net10.0", .. entry.Options, .. restore], repository, stdout, stderr, timeout.Token);
            Assert.True(code == 0, stderr.ToString());
            outputs.Add($"format: {format}\nexit: {code}\nstdout:\n{stdout}stderr:\n{stderr}");
        }
        await Verifier.Verify(string.Join("\n", outputs)).UseDirectory("Snapshots").UseFileName("serilog-msbuild").DisableDiff();
    }
}
