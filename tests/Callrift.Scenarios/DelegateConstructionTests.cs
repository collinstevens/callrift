using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DelegateConstructionTests
{
    [Theory]
    [InlineData(false, "new Func<int>(() => Work())")]
    [InlineData(false, "new(() => Work())")]
    [InlineData(false, "new Func<int>(Work)")]
    [InlineData(true, "new Func<int>(() => Work())")]
    [InlineData(true, "new(() => Work())")]
    [InlineData(true, "new Func<int>(Work)")]
    public async Task DelegateCreationPreservesPossibleCallbacks(bool workspace, string creation)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { public void Run() { Func<int> callback = " + creation + "; callback(); } int Work() { Before(); return 1; } void Before() {} void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("delegate-construction", "Valid delegate constructors preserve possible callback edges without unresolved-call diagnostics.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] selection = command == "diff" ? [] : ["--entry", "Flow.Run"];
            string[] target = command == "reach" ? ["--to", "Flow.After"] : [];
            string[] restore = workspace && command != "diff" ? ["--no-restore"] : [];
            var output = await fixture.RunAsync([command, .. revisions, .. selection, .. target, .. mode, .. restore, "--format", "json"]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDelegateTargetRemainsUnresolved(bool workspace)
    {
        var files = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { public Func<int> Run() => new Func<int>(Missing); }"
        };
        var after = new Dictionary<string, string>(files) { ["Flow.cs"] = files["Flow.cs"] + " class Unused {}" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("invalid-delegate", "An unknown delegate target remains an unresolved call.", files, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", .. mode, "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == "unresolved-call");
        var constructor = document.RootElement.GetProperty("trees")[0].GetProperty("children")[0];
        Assert.Equal("unresolved", constructor.GetProperty("after").GetProperty("binding").GetString());
    }
}
