using VerifyXunit;
using Xunit;

namespace Callrift.Scenarios;

public sealed class QueryTests
{
    public static IEnumerable<object[]> Queries =>
    [
        ["tree", "orders", new[] { "tree", "--entry", "OrdersController.Place", "--locs" }],
        ["reach", "orders", new[] { "reach", "--entry", "OrdersController.Place", "--to", "PricingClient.GetPriceAsync", "--locs" }],
        ["reach-limit", "branches", new[] { "reach", "--entry", "Flow.Run", "--to", "Flow.Save", "--max-paths", "1" }],
        ["reach-depth", "orders", new[] { "reach", "--entry", "OrdersController.Place", "--to", "PricingClient.GetPriceAsync", "--depth", "1" }],
        ["no-path", "guard", new[] { "reach", "--entry", "Flow.Save", "--to", "Flow.Run" }],
        ["tree-cycle", "new-recursion", new[] { "tree", "--entry", "Flow.Run" }],
        ["diff-locs", "orders", new[] { "diff", "HEAD^", "HEAD", "--locs", "--exit-code" }],
        ["strict", "top-level", new[] { "diff", "HEAD^", "HEAD", "--strict" }],
        ["strict-clean", "guard", new[] { "diff", "HEAD^", "HEAD", "--strict" }]
    ];

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query(string name, string scenarioName, string[] arguments)
    {
        await using var fixture = await GitFixture.CreateAsync(ScenarioCatalog.All.Single(s => s.Name == scenarioName));
        var outputs = new List<string>();
        foreach (var format in new[] { "text", "md", "json" })
            outputs.Add("format: " + format + "\n" + await fixture.RunAsync([.. arguments, "--format", format]));
        await Verifier.Verify(string.Join("\n", outputs).Replace(fixture.Before, "<before>", StringComparison.Ordinal).Replace(fixture.After, "<after>", StringComparison.Ordinal))
            .UseDirectory("Snapshots").UseFileName("query-" + name).DisableDiff();
    }

    [Fact]
    public async Task MergeBase()
    {
        var scenario = ScenarioCatalog.All.Single(s => s.Name == "guard");
        await using var fixture = await GitFixture.CreateAsync(scenario);
        await fixture.Git("branch", "feature", fixture.After);
        await fixture.Git("checkout", "-b", "mainline", fixture.Before);
        await fixture.WriteAsync(new Dictionary<string, string> { ["Unrelated.cs"] = "class Unrelated { public void Run() {} }" });
        await fixture.Git("add", ".");
        await fixture.Git("commit", "-S", "-m", "test: diverge mainline");
        var text = await fixture.RunAsync("diff", "mainline...feature");
        var json = await fixture.RunAsync("diff", "mainline...feature", "--format", "json");
        await Verifier.Verify((text + "\n" + json).Replace(fixture.Before, "<before>", StringComparison.Ordinal).Replace(fixture.After, "<after>", StringComparison.Ordinal))
            .UseDirectory("Snapshots").DisableDiff();
    }

    [Fact]
    public async Task InvalidQueries()
    {
        await using var fixture = await GitFixture.CreateAsync(ScenarioCatalog.All.Single(s => s.Name == "guard"));
        var outputs = new List<string>
        {
            await fixture.RunAsync("tree"),
            await fixture.RunAsync("reach", "--entry", "Flow.Run"),
            await fixture.RunAsync("reach", "--entry", "Flow.Run", "--to", "Flow.Save", "--max-paths", "0"),
            await fixture.RunAsync("diff", "HEAD^...HEAD", "--staged"),
            await fixture.RunAsync("tree", "HEAD^", "HEAD", "--entry", "Flow.Run")
        };
        await Verifier.Verify(string.Join("\n", outputs)).UseDirectory("Snapshots").DisableDiff();
    }
}
