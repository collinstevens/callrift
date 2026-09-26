using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DepthReachTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DistinguishesCompleteAndTruncatedAbsentPaths(bool hasCalls, bool workspace)
    {
        var source = "static class Entry { public static void Run() => Leaf(); static void Leaf() { " + (hasCalls ? "Hidden();" : "") + " } static void Hidden() {} } static class Unrelated { public static void Target() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source + " class Addition {}" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("depth-reach", "No-path results report incompleteness only when the depth bound omits calls.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["reach", fixture.Before, "--entry", "Entry.Run", "--to", "Unrelated.Target", "--depth", "1", "--format", "json", .. mode]);
        Assert.StartsWith("exit: 0\n", output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("paths").EnumerateArray());
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(hasCalls, document.RootElement.GetProperty("truncated").GetBoolean());
    }
}
