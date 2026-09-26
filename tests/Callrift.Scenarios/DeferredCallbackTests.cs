using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DeferredCallbackTests
{
    [Theory]
    [InlineData("public Action Run() => () => Work();")]
    [InlineData("public Action Run() { Action callback = () => Work(); return callback; }")]
    [InlineData("public Action Run() => delegate { Work(); };")]
    [InlineData("public Action Run() => Work;")]
    [Trait("Layer", "Fast")]
    public Task DeferredCreationRetainsPossibleCalls(string creation) => VerifyDeferredCreationRetainsPossibleCalls(false, creation);

    [Theory]
    [InlineData("public Action Run() => () => Work();")]
    [InlineData("public Action Run() { Action callback = () => Work(); return callback; }")]
    [InlineData("public Action Run() => delegate { Work(); };")]
    [InlineData("public Action Run() => Work;")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDeferredCreationRetainsPossibleCalls(string creation) => VerifyDeferredCreationRetainsPossibleCalls(true, creation);

    private static async Task VerifyDeferredCreationRetainsPossibleCalls(bool workspace, string creation)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { " + creation + " void Work() { Before(); } void Before() {} void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("deferred-callback", "Returning or assigning a callback retains potential reachability without claiming immediate execution.", before, after, []), workspace);
        var trees = await fixture.QueryFormatsAsync(new DiffOptions { Entries = ["Flow.Run"] });
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var options = new DiffOptions { Entries = command == "diff" ? [] : ["Flow.Run"] };
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : command == "tree" ? trees["json"] : await fixture.QueryAsync(options, target: "Flow.After");
            using var document = JsonDocument.Parse(output);
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
            var output = trees[format];
            Assert.Contains("callback", output);
            Assert.Contains("Flow.Work", output);
            Assert.Contains("Flow.After", output);
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task MethodGroupReceiverIsEvaluatedBeforeDeferredBody() => VerifyMethodGroupReceiverIsEvaluatedBeforeDeferredBody(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceMethodGroupReceiverIsEvaluatedBeforeDeferredBody() => VerifyMethodGroupReceiverIsEvaluatedBeforeDeferredBody(true);

    private static async Task VerifyMethodGroupReceiverIsEvaluatedBeforeDeferredBody(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "using System; class Flow { public Action Run() => GetWorker().Work; Worker GetWorker() => new Worker(); } class Worker { public void Work() { Before(); } void Before() {} void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("deferred-receiver", "Method-group receiver evaluation precedes its deferred callback.", before, after, []), workspace);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] });
        using var document = JsonDocument.Parse(output);
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
