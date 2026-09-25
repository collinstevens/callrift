using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class CallCollectionTests
{
    [Theory]
    [InlineData("switch-guard", false)]
    [InlineData("switch-guard", true)]
    [InlineData("delegate-factory", false)]
    [InlineData("delegate-factory", true)]
    [InlineData("converted-callback", false)]
    [InlineData("converted-callback", true)]
    public async Task NestedExpressionsRemainReachable(string name, bool workspace)
    {
        var original = ScenarioCatalog.All.Single(s => s.Name == name);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var scenario = original with
        {
            Before = new Dictionary<string, string>(original.Before) { ["App.csproj"] = project },
            After = new Dictionary<string, string>(original.After) { ["App.csproj"] = project }
        };
        await using var fixture = await GitFixture.CreateAsync(scenario);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] selection = command == "diff" ? [] : ["--entry", "Flow.Run"];
            string[] target = command == "reach" ? ["--to", "Flow.After"] : [];
            foreach (var format in new[] { "text", "md", "json" })
            {
                string[] restore = workspace && (command != "diff" || format != "text") ? ["--no-restore"] : [];
                var output = await fixture.RunAsync([command, .. revisions, .. selection, .. target, .. mode, .. restore, "--locs", "--format", format]);
                Assert.StartsWith("exit: 0\n", output);
                Assert.Contains("Flow.Run", output);
                Assert.Contains("Flow.After", output);
                Assert.DoesNotContain("unresolved-call", output);
                if (command == "diff") Assert.Contains("Flow.Before", output);
                if (format != "json") continue;
                using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
                Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
                Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
                var children = root.GetProperty("children").EnumerateArray().ToArray();
                if (name == "switch-guard")
                {
                    var branch = Assert.Single(children);
                    Assert.Equal("case string text when Check(text):", branch.GetProperty("label").GetString());
                    var guard = branch.GetProperty("children")[0];
                    Assert.Equal("Flow.Check", guard.GetProperty("label").GetString());
                    Assert.Equal(7, guard.GetProperty("after").GetProperty("callSites")[0].GetProperty("startLine").GetInt32());
                    if (command != "reach") Assert.Equal("Flow.Save", branch.GetProperty("children")[1].GetProperty("label").GetString());
                }
                else if (name == "delegate-factory")
                {
                    Assert.Equal("Flow.Factory", Assert.Single(children).GetProperty("label").GetString());
                }
                else
                {
                    var callback = Assert.Single(children).GetProperty("children")[0];
                    Assert.Equal("Flow.Work", callback.GetProperty("label").GetString());
                    Assert.Equal("callback", callback.GetProperty("after").GetProperty("relation").GetString());
                    Assert.Equal(4, callback.GetProperty("after").GetProperty("callSites")[0].GetProperty("startLine").GetInt32());
                }
            }
        }
    }
}
