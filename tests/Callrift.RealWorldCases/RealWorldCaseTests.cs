using System.Globalization;
using Callrift.Cli;
using Callrift.Core;
using Callrift.MSBuild;
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
        var (diffOptions, workspace) = ParseOptions(options);
        IAnalysisProvider provider = workspace is null ? new SourceOnlyAnalysisProvider() : new MSBuildAnalysisProvider(workspace);
        var result = await new CallriftService(provider).DiffAsync(new DiffRequest(repository, entry.Before, entry.After)
        {
            Options = diffOptions
        }, timeout.Token);
        var diagnostics = string.Concat(result.Diagnostics.Take(8).Select(diagnostic =>
            (diagnostic.Location is null ? "" : $"{diagnostic.Location.Path}:{diagnostic.Location.Line}: ") + $"{diagnostic.Code}: {diagnostic.Message}\n"));
        if (result.Diagnostics.Count > 8)
            diagnostics += $"{result.Diagnostics.Count - 8} additional diagnostics; use --diagnostics full.\n";
        string Output(string rendered) => $"exit: 0\nstdout:\n{rendered}stderr:\n{diagnostics}";
        var outputs = new[]
        {
            "format: text\n" + Output(DiffRenderer.Render(result, diffOptions)),
            "format: md\n" + Output(DiffRenderer.Render(result, diffOptions, markdown: true))
        };
        await Task.WhenAll(VerifyAsync(string.Join("\n", outputs), name),
            VerifyAsync(Output(JsonRenderer.Render(result)), name + "-json"));
    }

    private static (DiffOptions Options, MSBuildOptions? Workspace) ParseOptions(string[] arguments)
    {
        var entries = new List<string>();
        var files = new List<string>();
        var depth = 6;
        var externals = false;
        string? project = null;
        string? framework = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument == "--externals")
            {
                externals = true;
                continue;
            }
            if (++index == arguments.Length) throw new ArgumentException($"Missing value for {argument}.", nameof(arguments));
            var value = arguments[index];
            switch (argument)
            {
                case "--entry": entries.Add(value); break;
                case "--file": files.Add(value); break;
                case "--depth": depth = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "--project": project = value; break;
                case "--framework": framework = value; break;
                default: throw new ArgumentException($"Unsupported snapshot option: {argument}.", nameof(arguments));
            }
        }
        if (framework is not null && project is null) throw new ArgumentException("A framework requires a project.", nameof(arguments));
        return (new DiffOptions { Entries = entries, Files = files, MaxDepth = depth, IncludeExternals = externals },
            project is null ? null : new MSBuildOptions(project, framework));
    }

    private static async Task VerifyAsync(string value, string name) =>
        await Verifier.Verify(value).UseDirectory("Snapshots").UseFileName(name).DisableDiff();
}
