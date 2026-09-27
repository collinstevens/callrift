using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticInitializationDepthTests
{
    [Fact]
    [Trait("Layer", "Fast")]
    public Task CompletedInitializationDoesNotCauseLaterChangeMarkers() => VerifyCompletedInitializationDoesNotCauseLaterChangeMarkers(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceCompletedInitializationDoesNotCauseLaterChangeMarkers() => VerifyCompletedInitializationDoesNotCauseLaterChangeMarkers(true);

    private static async Task VerifyCompletedInitializationDoesNotCauseLaterChangeMarkers(bool workspace)
    {
        const string source = "static class State { static State() { Sink.Before(); } public static void Touch() { Sink.Body(); } } static class Other { public static void Run() { State.Touch(); } } static class Entry { public static void Run() { State.Touch(); Other.Run(); } } static class Sink { public static void Before() {} public static void After() {} public static void Body() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("static-depth", "Depth limits preserve initialization changes without attributing them to later calls after initialization.", before, after, []), workspace);
        var output = await fixture.DiffAsync(new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 2 });
        using var document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.True(document.RootElement.GetProperty("hasChanges").GetBoolean());
        var nodes = Flatten(document.RootElement.GetProperty("trees")).ToArray();
        var touches = nodes.Where(node => node.GetProperty("label").GetString() == "State.Touch").ToArray();
        Assert.Equal(2, touches.Length);
        Assert.All(touches, node => Assert.Equal("unchanged", node.GetProperty("change").GetString()));
        Assert.Contains(touches, node => node.GetProperty("detail").GetString() == "depth limit");
        Assert.Contains(nodes, node => node.GetProperty("label").GetString() == "initialization of State" && node.GetProperty("detail").GetString() == "changes below depth limit");
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes) => nodes.EnumerateArray().SelectMany(node => new[] { node }.Concat(Flatten(node.GetProperty("children"))));
}
