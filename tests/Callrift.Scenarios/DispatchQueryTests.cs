using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DispatchQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedContractSignatureKeepsItsRoot(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "interface Contract { void Run(int value); } class Worker : Contract { public void Run(int value) {} } class Other : Contract { public void Run(int value) {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("int value", "long value", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("contract-signature", "A uniquely paired contract signature retains a single modified root.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            string[] restore = workspace && format != "text" ? ["--no-restore"] : [];
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--entry", "Contract.Run", .. mode, .. restore, "--format", format]);
            Assert.StartsWith("exit: 0\n", output);
            Assert.Contains("signature changed", output);
            if (format != "json") continue;
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
            Assert.Equal("modified", root.GetProperty("change").GetString());
            Assert.EndsWith("::Contract.Run(int)", root.GetProperty("before").GetProperty("symbolId").GetString());
            Assert.EndsWith("::Contract.Run(long)", root.GetProperty("after").GetProperty("symbolId").GetString());
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task SelectedContractIncludesPossibleImplementations(bool workspace, bool abstractContract, bool multiple)
    {
        var contract = abstractContract ? "abstract class Contract { public abstract void Run(); }" : "interface Contract { void Run(); }";
        var implementation = abstractContract ? "public override void Run()" : "public void Run()";
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = contract + " class Worker : Contract { " + implementation + " { Before(); } void Before() {} void After() {} }"
        };
        if (multiple) before["Flow.cs"] += " class Other : Contract { " + implementation + " {} }";
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("selected-contract", "Selected contracts expose possible source implementations.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "tree", "reach", "diff" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] target = command == "reach" ? ["--to", "Worker.After"] : [];
            foreach (var format in new[] { "text", "md", "json" })
            {
                string[] restore = workspace && format != "text" ? ["--no-restore"] : [];
                var output = await fixture.RunAsync([command, .. revisions, "--entry", "Contract.Run", .. target, .. mode, .. restore, "--format", format]);
                Assert.StartsWith("exit: 0\n", output);
                Assert.Contains("Contract.Run", output);
                Assert.Contains("Worker.After", output);
                if (command == "diff") Assert.Contains("Worker.Before", output);
                if (format != "json") continue;
                using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
                var root = document.RootElement.GetProperty(command == "reach" ? "paths" : "trees")[0];
                Assert.Equal("member", root.GetProperty("kind").GetString());
                var side = root.GetProperty("after");
                Assert.Equal("possible", side.GetProperty("dispatch").GetString());
                Assert.Equal("definition", side.GetProperty("relation").GetString());
                Assert.Empty(side.GetProperty("callSites").EnumerateArray());
                Assert.EndsWith("::Contract.Run()", side.GetProperty("symbolId").GetString());
                var targets = side.GetProperty("targetIds").EnumerateArray().Select(value => value.GetString()!).ToArray();
                Assert.Equal(multiple ? 2 : 1, targets.Length);
                Assert.Contains(targets, value => value.EndsWith("::Worker.Run()", StringComparison.Ordinal));
                if (multiple) Assert.Contains(targets, value => value.EndsWith("::Other.Run()", StringComparison.Ordinal));
            }
        }
    }
}
