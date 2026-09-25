using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DeferredCallbackTests
{
    [Theory]
    [InlineData(false, "public Action Run() => () => Work();")]
    [InlineData(false, "public Action Run() { Action callback = () => Work(); return callback; }")]
    [InlineData(false, "public Action Run() => delegate { Work(); };")]
    [InlineData(false, "public Action Run() => Work;")]
    [InlineData(true, "public Action Run() => () => Work();")]
    [InlineData(true, "public Action Run() { Action callback = () => Work(); return callback; }")]
    [InlineData(true, "public Action Run() => delegate { Work(); };")]
    [InlineData(true, "public Action Run() => Work;")]
    public async Task DeferredCreationRetainsPossibleCalls(bool workspace, string creation)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { " + creation + " void Work() { Before(); } void Before() {} void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("deferred-callback", "Returning or assigning a callback retains potential reachability without claiming immediate execution.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] entry = command == "diff" ? [] : ["--entry", "Flow.Run"];
            string[] target = command == "reach" ? ["--to", "Flow.After"] : [];
            var output = await fixture.RunAsync([command, .. revisions, .. entry, .. target, .. mode, "--format", "json"]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
            var callback = Assert.Single(root.GetProperty("children").EnumerateArray());
            Assert.Equal("callback", callback.GetProperty("after").GetProperty("relation").GetString());
            Assert.Contains("Flow.Work", output);
            Assert.Contains("Flow.After", output);
            if (command == "diff") Assert.Contains("Flow.Before", output);
        }
        foreach (var format in new[] { "text", "md" })
        {
            var output = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", .. mode, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("callback", output);
            Assert.Contains("Flow.Work", output);
            Assert.Contains("Flow.After", output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MethodGroupReceiverIsEvaluatedBeforeDeferredBody(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { public Action Run() => GetWorker().Work; Worker GetWorker() => new Worker(); } class Worker { public void Work() { Before(); } void Before() {} void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("deferred-receiver", "Method-group receiver evaluation precedes its deferred callback.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", .. mode, "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var children = document.RootElement.GetProperty("trees")[0].GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("Flow.GetWorker", children[0].GetProperty("label").GetString());
        Assert.Equal("call", children[0].GetProperty("after").GetProperty("relation").GetString());
        Assert.Equal("callback", children[1].GetProperty("after").GetProperty("relation").GetString());
        Assert.Equal("Worker.Work", Assert.Single(children[1].GetProperty("children").EnumerateArray()).GetProperty("label").GetString());
        Assert.Contains("Worker.After", output);
    }
}
