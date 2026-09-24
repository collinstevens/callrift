using Callrift.Cli;
using VerifyXunit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Callrift.Corpus;

public sealed class CorpusTests
{
    public static IEnumerable<object[]> Entries => CorpusStore.ReadManifest().Select(e => new object[] { e.Id });

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task PinnedHistory(string id)
    {
        var entry = CorpusStore.ReadManifest().Single(e => e.Id == id);
        var repository = await CorpusStore.PrepareAsync(entry);
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
