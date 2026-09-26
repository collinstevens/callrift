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

    private static bool Routine => Environment.GetEnvironmentVariable("CALLRIFT_CASE_SET") switch
    {
        null or "" or "all" => false,
        "routine" => true,
        var value => throw new InvalidOperationException($"Unknown case set: {value}. Use all or routine.")
    };

    private static IEnumerable<RealWorldCase> SelectedEntries => RealWorldCaseStore.ReadManifest().Where(e => !Routine || e.Routine);

    public static IEnumerable<object[]> Entries => SelectedEntries.Where(e => e.Views is null).Select(e => new object[] { e.Id });

    public static IEnumerable<object[]> Views => SelectedEntries
        .SelectMany(e => (e.Views ?? []).Where(v => !Routine || v.Routine).Select(v => new object[] { e.Id, v.Id }));

    [Theory]
    [MemberData(nameof(Views))]
    public async Task ReviewedView(string id, string viewId)
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == id);
        var view = entry.Views!.Single(v => v.Id == viewId);
        Assert.NotNull(entry.Review);
        Assert.True(File.Exists(Path.Combine(RealWorldCaseStore.FindRoot(), "real-world-cases", entry.Review)));
        Assert.NotNull(entry.BeforeLicenseBlob);
        Assert.NotNull(entry.AfterLicenseBlob);
        await SnapshotAsync(entry, [.. entry.Options, .. view.Options], id + "-" + viewId);
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task PinnedHistory(string id)
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == id);
        await SnapshotAsync(entry, entry.Options, id);
    }

    private static async Task SnapshotAsync(RealWorldCase entry, string[] options, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var repository = await RealWorldCaseStore.PrepareAsync(entry, timeout.Token);
        var outputs = new List<string>();
        foreach (var format in new[] { "text", "md" })
        {
            using var stdout = new StringWriter { NewLine = "\n" };
            using var stderr = new StringWriter { NewLine = "\n" };
            var code = await CommandRunner.RunAsync(["diff", entry.Before, entry.After, "--format", format, "--color", "never", .. options], repository, stdout, stderr, timeout.Token);
            Assert.True(code == 0, stderr.ToString());
            outputs.Add($"format: {format}\nexit: {code}\nstdout:\n{stdout}stderr:\n{stderr}");
        }
        using var jsonOutput = new StringWriter { NewLine = "\n" };
        using var jsonError = new StringWriter { NewLine = "\n" };
        var jsonCode = await CommandRunner.RunAsync(["diff", entry.Before, entry.After, "--format", "json", .. options], repository, jsonOutput, jsonError, timeout.Token);
        Assert.True(jsonCode == 0, jsonError.ToString());
        await Task.WhenAll(VerifyAsync(string.Join("\n", outputs), name),
            VerifyAsync($"exit: {jsonCode}\nstdout:\n{jsonOutput}stderr:\n{jsonError}", name + "-json"));
    }

    private static async Task VerifyAsync(string value, string name) =>
        await Verifier.Verify(value).UseDirectory("Snapshots").UseFileName(name).DisableDiff();
}
