using VerifyXunit;
using Xunit;

[assembly: CollectionBehavior(MaxParallelThreads = 4)]

namespace Callrift.Scenarios;

public sealed class ScenarioTests
{
    public static IEnumerable<object[]> Cases => ScenarioCatalog.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CallFlow(string name)
    {
        var scenario = ScenarioCatalog.All.Single(s => s.Name == name);
        await using var fixture = await GitFixture.CreateAsync(scenario);
        var text = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. scenario.Options]);
        var markdown = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", "md", .. scenario.Options]);
        await Verifier.Verify(scenario.Description + "\n\n" + text + "\nmarkdown:\n" + markdown)
            .UseDirectory("Snapshots").UseFileName(name).DisableDiff();
        var json = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", "json", .. scenario.Options]);
        await Verifier.Verify(json.Replace(fixture.Before, "<before>", StringComparison.Ordinal).Replace(fixture.After, "<after>", StringComparison.Ordinal))
            .UseDirectory("Snapshots").UseFileName(name + "-json").DisableDiff();
    }

    [Fact]
    public async Task WorkingTreeAndIndex()
    {
        var scenario = ScenarioCatalog.All.Single(s => s.Name == "guard");
        await using var fixture = await GitFixture.CreateAsync(scenario);
        await fixture.Git("reset", "--soft", fixture.Before);
        await fixture.WriteAsync(scenario.Before);
        var staged = await fixture.RunAsync("diff", "--staged");
        var working = await fixture.RunAsync();
        await Verifier.Verify("staged:\n" + staged + "\nworking:\n" + working).UseDirectory("Snapshots").DisableDiff();
        var stagedJson = await fixture.RunAsync("diff", "--staged", "--format", "json");
        var workingJson = await fixture.RunAsync("diff", "--format", "json");
        await Verifier.Verify(("staged:\n" + stagedJson + "\nworking:\n" + workingJson).Replace(fixture.Before, "<before>", StringComparison.Ordinal))
            .UseDirectory("Snapshots").UseFileName("working-index-json").DisableDiff();
    }

    [Fact]
    public async Task InvalidSelection()
    {
        await using var fixture = await GitFixture.CreateAsync(ScenarioCatalog.All.Single(s => s.Name == "overload-recursion"));
        var ambiguous = await fixture.RunAsync("diff", fixture.Before, fixture.After, "--entry", "Flow.Save");
        var missing = await fixture.RunAsync("diff", "--entry", "Missing");
        await Verifier.Verify(ambiguous + "\n" + missing).UseDirectory("Snapshots").DisableDiff();
    }
}
