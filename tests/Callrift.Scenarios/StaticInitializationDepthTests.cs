using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticInitializationDepthTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedInitializationDoesNotCauseLaterChangeMarkers(bool workspace)
    {
        const string source = "static class State { static State() { Sink.Before(); } public static void Touch() { Sink.Body(); } } static class Other { public static void Run() { State.Touch(); } } static class Entry { public static void Run() { State.Touch(); Other.Run(); } } static class Sink { public static void Before() {} public static void After() {} public static void Body() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("static-depth", "Depth limits preserve initialization changes without attributing them to later calls after initialization.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--entry", "Entry.Run", "--depth", "2", "--format", "json", .. mode]);
        Assert.StartsWith("exit: 0\n", output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
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
