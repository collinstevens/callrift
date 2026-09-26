using System.Globalization;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class AnalysisFixture : IAsyncDisposable
{
    private readonly GitFixture? workspace;
    private readonly CallGraph? before;
    private readonly CallGraph? after;
    private readonly bool includeTests;
    private readonly HashSet<string> restored = [];

    private AnalysisFixture(GitFixture workspace, bool includeTests)
    {
        this.workspace = workspace;
        this.includeTests = includeTests;
    }

    private AnalysisFixture(CallGraph before, CallGraph after, bool includeTests)
    {
        this.before = before;
        this.after = after;
        this.includeTests = includeTests;
    }

    public static async Task<AnalysisFixture> CreateAsync(Scenario scenario, bool workspace, bool includeTests = false)
    {
        if (workspace) return new AnalysisFixture(await GitFixture.CreateAsync(scenario), includeTests);
        var (before, after) = await SourceFixture.AnalyzeAsync(scenario, includeTests);
        return new AnalysisFixture(before, after, includeTests);
    }

    public async Task<string> DiffAsync(DiffOptions? options = null, bool reverse = false)
    {
        options ??= new DiffOptions { IncludeTests = includeTests };
        Validate(options);
        if (workspace is null)
            return JsonRenderer.Render(CallriftService.Compare(reverse ? after! : before!, reverse ? before! : after!, options));
        string[] revisions = reverse ? [workspace.After, workspace.Before] : [workspace.Before, workspace.After];
        return await RunAsync("diff", revisions, options, []);
    }

    public async Task<IReadOnlyDictionary<string, string>> DiffFormatsAsync(DiffOptions options, bool markdownAlias = false)
    {
        Validate(options);
        if (workspace is null)
        {
            var result = CallriftService.Compare(before!, after!, options);
            return RenderFormats(result, options, markdownAlias ? "markdown" : "md");
        }
        var outputs = new Dictionary<string, string>();
        foreach (var format in new[] { "json", "text", markdownAlias ? "markdown" : "md" })
            outputs[format] = await RunAsync("diff", [workspace.Before, workspace.After], options, [], format);
        return outputs;
    }

    public async Task<string> QueryAsync(DiffOptions options, bool before = false, string? target = null, int maxPaths = 100)
    {
        Validate(options);
        if (workspace is null)
            return JsonRenderer.Render(CallQueries.Query(before ? this.before! : after!, new QueryRequest("unused")
            { Options = options, Target = target, MaxPaths = maxPaths }));
        string[] query = target is null ? [] : ["--to", target, "--max-paths", maxPaths.ToString(CultureInfo.InvariantCulture)];
        return await RunAsync(target is null ? "tree" : "reach", [before ? workspace.Before : workspace.After], options, query);
    }

    public async Task<IReadOnlyDictionary<string, string>> QueryFormatsAsync(DiffOptions options, bool before = false, string? target = null, int maxPaths = 100)
    {
        Validate(options);
        if (workspace is null)
        {
            var result = CallQueries.Query(before ? this.before! : after!, new QueryRequest("unused")
            { Options = options, Target = target, MaxPaths = maxPaths });
            return RenderFormats(result, options, "md");
        }
        var outputs = new Dictionary<string, string>();
        string[] query = target is null ? [] : ["--to", target, "--max-paths", maxPaths.ToString(CultureInfo.InvariantCulture)];
        foreach (var format in new[] { "text", "md", "json" })
            outputs[format] = await RunAsync(target is null ? "tree" : "reach", [before ? workspace.Before : workspace.After], options, query, format);
        return outputs;
    }

    private static IReadOnlyDictionary<string, string> RenderFormats(DiffResult result, DiffOptions options, string markdownFormat) =>
        new Dictionary<string, string>
        {
            ["text"] = DiffRenderer.Render(result, options),
            [markdownFormat] = DiffRenderer.Render(result, options, markdown: true),
            ["json"] = JsonRenderer.Render(result)
        };

    private async Task<string> RunAsync(string command, string[] revisions, DiffOptions options, string[] query, string format = "json")
    {
        var arguments = new List<string> { command };
        arguments.AddRange(revisions);
        arguments.AddRange(["--project", "App.csproj", "--format", format, "--depth", options.MaxDepth.ToString(CultureInfo.InvariantCulture),
            "--context", options.Context < 0 ? "all" : options.Context.ToString(CultureInfo.InvariantCulture)]);
        foreach (var entry in options.Entries) arguments.AddRange(["--entry", entry]);
        foreach (var file in options.Files) arguments.AddRange(["--file", file]);
        if (options.IncludeExternals) arguments.Add("--externals");
        if (options.IncludeTests) arguments.Add("--tests");
        if (options.Locations) arguments.Add("--locs");
        if (revisions.All(restored.Contains)) arguments.Add("--no-restore");
        arguments.AddRange(query);
        if (options.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(options.Paths);
        }
        var output = await workspace!.RunAsync(arguments.ToArray());
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        restored.UnionWith(revisions);
        return output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0];
    }

    private void Validate(DiffOptions options)
    {
        if (options.IncludeTests != includeTests)
            throw new ArgumentException("The fixture's analyzed test inclusion must match the query options.", nameof(options));
    }

    public ValueTask DisposeAsync() => workspace?.DisposeAsync() ?? ValueTask.CompletedTask;
}
