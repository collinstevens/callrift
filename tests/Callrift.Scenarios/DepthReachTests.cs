using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DepthReachTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task DistinguishesCompleteAndTruncatedAbsentPaths(bool hasCalls) => VerifyDistinguishesCompleteAndTruncatedAbsentPaths(hasCalls, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDistinguishesCompleteAndTruncatedAbsentPaths(bool hasCalls) => VerifyDistinguishesCompleteAndTruncatedAbsentPaths(hasCalls, true);

    private static async Task VerifyDistinguishesCompleteAndTruncatedAbsentPaths(bool hasCalls, bool workspace)
    {
        var source = "static class Entry { public static void Run() => Leaf(); static void Leaf() { " + (hasCalls ? "Hidden();" : "") + " } static void Hidden() {} } static class Unrelated { public static void Target() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source + " class Addition {}" };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("depth-reach", "No-path results report incompleteness only when the depth bound omits calls.", before, after, []), workspace);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 1 }, before: true, target: "Unrelated.Target");
        using var document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("paths").EnumerateArray());
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(hasCalls, document.RootElement.GetProperty("truncated").GetBoolean());
    }
}
