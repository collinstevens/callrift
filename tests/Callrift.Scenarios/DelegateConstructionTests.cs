using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DelegateConstructionTests
{
    [Theory]
    [InlineData("new Func<int>(() => Work())")]
    [InlineData("new(() => Work())")]
    [InlineData("new Func<int>(Work)")]
    [Trait("Layer", "Fast")]
    public Task DelegateCreationPreservesPossibleCallbacks(string creation) => VerifyDelegateCreationPreservesPossibleCallbacks(false, creation);

    [Theory]
    [InlineData("new Func<int>(() => Work())")]
    [InlineData("new(() => Work())")]
    [InlineData("new Func<int>(Work)")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDelegateCreationPreservesPossibleCallbacks(string creation) => VerifyDelegateCreationPreservesPossibleCallbacks(true, creation);

    private static async Task VerifyDelegateCreationPreservesPossibleCallbacks(bool workspace, string creation)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { public void Run() { Func<int> callback = " + creation + "; callback(); } int Work() { Before(); return 1; } void Before() {} void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("delegate-construction", "Valid delegate constructors preserve possible callback edges without unresolved-call diagnostics.", before, after, []), workspace);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var options = new DiffOptions { Entries = command == "diff" ? [] : ["Flow.Run"] };
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Flow.After" : null);
            using var document = JsonDocument.Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
            var constructor = root.GetProperty("children")[0];
            Assert.Equal("resolved", constructor.GetProperty("after").GetProperty("binding").GetString());
            var callback = Assert.Single(constructor.GetProperty("children").EnumerateArray());
            Assert.Equal("Flow.Work", callback.GetProperty("label").GetString());
            Assert.Equal("callback", callback.GetProperty("after").GetProperty("relation").GetString());
            Assert.Contains("Flow.After", output);
            if (command == "diff") Assert.Contains("Flow.Before", output);
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task MissingDelegateTargetRemainsUnresolved() => VerifyMissingDelegateTargetRemainsUnresolved(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceMissingDelegateTargetRemainsUnresolved() => VerifyMissingDelegateTargetRemainsUnresolved(true);

    private static async Task VerifyMissingDelegateTargetRemainsUnresolved(bool workspace)
    {
        var files = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { public Func<int> Run() => new Func<int>(Missing); }"
        };
        var after = new Dictionary<string, string>(files) { ["Flow.cs"] = files["Flow.cs"] + " class Unused {}" };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("invalid-delegate", "An unknown delegate target remains an unresolved call.", files, after, []), workspace);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] });
        using var document = JsonDocument.Parse(output);
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == "unresolved-call");
        var constructor = document.RootElement.GetProperty("trees")[0].GetProperty("children")[0];
        Assert.Equal("unresolved", constructor.GetProperty("after").GetProperty("binding").GetString());
    }
}
