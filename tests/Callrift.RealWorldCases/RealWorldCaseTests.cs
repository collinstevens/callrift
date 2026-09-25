using Callrift.Cli;
using VerifyXunit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Callrift.RealWorldCases;

public sealed class RealWorldCaseTests
{
    [Fact]
    public async Task RestoredSerilog()
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == "serilog-alignment-guard");
        var repository = await RealWorldCaseStore.PrepareAsync(entry);
        var outputs = new List<string>();
        foreach (var format in new[] { "text", "md", "json" })
        {
            using var stdout = new StringWriter { NewLine = "\n" };
            using var stderr = new StringWriter { NewLine = "\n" };
            var code = await CommandRunner.RunAsync(["diff", entry.Before, entry.After, "--format", format,
                "--project", "src/Serilog/Serilog.csproj", "--framework", "net10.0", .. entry.Options], repository, stdout, stderr);
            outputs.Add($"format: {format}\nexit: {code}\nstdout:\n{stdout}stderr:\n{stderr}");
        }
        await Verifier.Verify(string.Join("\n", outputs)).UseDirectory("Snapshots").UseFileName("serilog-msbuild").DisableDiff();
    }

    public static IEnumerable<object[]> Entries => RealWorldCaseStore.ReadManifest().Select(e => new object[] { e.Id });

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task PinnedHistory(string id)
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == id);
        var repository = await RealWorldCaseStore.PrepareAsync(entry);
        var outputs = new List<string>();
        foreach (var format in new[] { "text", "md" })
        {
            using var stdout = new StringWriter { NewLine = "\n" };
            using var stderr = new StringWriter { NewLine = "\n" };
            var code = await CommandRunner.RunAsync(["diff", entry.Before, entry.After, "--format", format, "--color", "never", .. entry.Options], repository, stdout, stderr);
            outputs.Add($"format: {format}\nexit: {code}\nstdout:\n{stdout}stderr:\n{stderr}");
        }
        await Verifier.Verify(string.Join("\n", outputs)).UseDirectory("Snapshots").UseFileName(id).DisableDiff();
        using var jsonOutput = new StringWriter { NewLine = "\n" };
        using var jsonError = new StringWriter { NewLine = "\n" };
        var jsonCode = await CommandRunner.RunAsync(["diff", entry.Before, entry.After, "--format", "json", .. entry.Options], repository, jsonOutput, jsonError);
        await Verifier.Verify($"exit: {jsonCode}\nstdout:\n{jsonOutput}stderr:\n{jsonError}")
            .UseDirectory("Snapshots").UseFileName(id + "-json").DisableDiff();
    }
}
