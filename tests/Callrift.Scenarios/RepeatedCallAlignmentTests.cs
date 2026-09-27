using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class RepeatedCallAlignmentTests
{
    [Theory]
    [InlineData("Sink.Call", false)]
    [InlineData("Sink.Call", true)]
    [InlineData("new Item", false)]
    [InlineData("new Item", true)]
    [InlineData("Missing.Call", false)]
    [InlineData("Missing.Call", true)]
    [Trait("Layer", "Fast")]
    public async Task RetainedCallKeepsItsLocation(string call, bool insertion)
    {
        var (before, after) = await SourceFixture.AnalyzeAsync(Change(call, insertion));
        var options = new DiffOptions { Entries = ["Flow.Run"], Locations = true };
        var result = CallriftService.Compare(before, after, options);
        var root = Assert.Single(result.Trees);
        var changed = Assert.Single(root.Children, node => node.Mark == (insertion ? '+' : '-'));
        Assert.Equal(5, Assert.Single((insertion ? changed.After : changed.Before)!.CallSites).Line);
        var retained = Assert.Single(root.Children, node => node.Mark == ' ');
        Assert.Equal(insertion ? 5 : 6, Assert.Single(retained.Before!.CallSites).Line);
        Assert.Equal(insertion ? 6 : 5, Assert.Single(retained.After!.CallSites).Line);
        AssertLocation(JsonRenderer.Render(result), insertion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task WorkspaceRetainedCallKeepsItsLocation(bool insertion)
    {
        await using var fixture = await AnalysisFixture.CreateWorkspaceCliAsync(Change("new Item", insertion));
        var output = await fixture.DiffFormatsAsync(new DiffOptions { Entries = ["Flow.Run"], Locations = true });
        AssertLocation(output["json"], insertion);
        foreach (var format in new[] { "text", "md" })
            Assert.Contains((insertion ? "+" : "-") + " ├─ new Item [Flow.cs:5]", output[format]);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public async Task WhitespaceDoesNotShiftRetainedCall()
    {
        var scenario = Change("Sink.Call", false);
        var afterFiles = scenario.After.ToDictionary(pair => pair.Key, pair => pair.Value);
        afterFiles["Flow.cs"] = afterFiles["Flow.cs"].Replace("Sink.Call(\"retained\")", "Sink . Call ( \"retained\" )", StringComparison.Ordinal);
        var (before, after) = await SourceFixture.AnalyzeAsync(scenario with { After = afterFiles });
        var result = CallriftService.Compare(before, after, new DiffOptions { Entries = ["Flow.Run"], Locations = true });
        AssertLocation(JsonRenderer.Render(result), false);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public async Task ArgumentChangesPreserveBothSemanticMatches()
    {
        var scenario = Change("Sink.Call", false);
        var afterFiles = scenario.Before.ToDictionary(pair => pair.Key, pair => pair.Value);
        afterFiles["Flow.cs"] = afterFiles["Flow.cs"].Replace("\"removed\"", "\"retained\"", StringComparison.Ordinal)
            .Replace("Sink.Call(\"retained\");\n    }", "Sink.Call(\"changed\");\n    }", StringComparison.Ordinal);
        var (before, after) = await SourceFixture.AnalyzeAsync(scenario with { After = afterFiles });
        var root = Assert.Single(CallriftService.Compare(before, after, new DiffOptions { Entries = ["Flow.Run"] }).Trees);
        Assert.Equal(2, root.Children.Count);
        Assert.All(root.Children, node => Assert.Equal(' ', node.Mark));
        Assert.Equal('~', root.Mark);
    }

    private static void AssertLocation(string output, bool insertion)
    {
        using var json = JsonDocument.Parse(output);
        var changed = Assert.Single(json.RootElement.GetProperty("trees")[0].GetProperty("children").EnumerateArray(),
            node => node.GetProperty("change").GetString() == (insertion ? "added" : "removed"));
        Assert.Equal(5, changed.GetProperty(insertion ? "after" : "before").GetProperty("callSites")[0].GetProperty("startLine").GetInt32());
    }

    private static Scenario Change(string call, bool insertion)
    {
        var longer = $$"""
            static class Flow
            {
                public static void Run()
                {
                    {{call}}("removed");
                    {{call}}("retained");
                }
            }
            static class Sink { public static void Call(string value) {} }
            sealed class Item { public Item(string value) {} }
            """;
        var shorter = longer.Replace($"        {call}(\"removed\");\n", "", StringComparison.Ordinal);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        return new Scenario("repeated-call-alignment", "Repeated call additions and removals retain the source location of the matching invocation.",
            new Dictionary<string, string> { ["Flow.cs"] = insertion ? shorter : longer, ["App.csproj"] = project },
            new Dictionary<string, string> { ["Flow.cs"] = insertion ? longer : shorter, ["App.csproj"] = project }, []);
    }
}
