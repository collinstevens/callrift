using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DispatchQueryTests
{
    [Fact]
    [Trait("Layer", "Fast")]
    public Task SelectedContractSignatureKeepsItsRoot() => VerifySelectedContractSignatureKeepsItsRoot(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceSelectedContractSignatureKeepsItsRoot() => VerifySelectedContractSignatureKeepsItsRoot(true);

    private static async Task VerifySelectedContractSignatureKeepsItsRoot(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "interface Contract { void Run(int value); } class Worker : Contract { public void Run(int value) {} } class Other : Contract { public void Run(int value) {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("int value", "long value", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("contract-signature", "A uniquely paired contract signature retains a single modified root.", before, after, []), workspace);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions { Entries = ["Contract.Run"] });
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = outputs[format];
            Assert.Contains("signature changed", output);
            if (format != "json") continue;
            using var document = JsonDocument.Parse(output);
            var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
            Assert.Equal("modified", root.GetProperty("change").GetString());
            Assert.EndsWith("::Contract.Run(int)", root.GetProperty("before").GetProperty("symbolId").GetString());
            Assert.EndsWith("::Contract.Run(long)", root.GetProperty("after").GetProperty("symbolId").GetString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Layer", "Fast")]
    public Task SelectedContractIncludesPossibleImplementations(bool abstractContract, bool multiple) => VerifySelectedContractIncludesPossibleImplementations(false, abstractContract, multiple);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceSelectedContractIncludesPossibleImplementations(bool abstractContract, bool multiple) => VerifySelectedContractIncludesPossibleImplementations(true, abstractContract, multiple);

    private static async Task VerifySelectedContractIncludesPossibleImplementations(bool workspace, bool abstractContract, bool multiple)
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
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("selected-contract", "Selected contracts expose possible source implementations.", before, after, []), workspace);
        foreach (var command in new[] { "tree", "reach", "diff" })
        {
            var options = new DiffOptions { Entries = ["Contract.Run"] };
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Worker.After" : null);
            foreach (var format in new[] { "text", "md", "json" })
            {
                var output = outputs[format];
                Assert.Contains("Contract.Run", output);
                Assert.Contains("Worker.After", output);
                if (command == "diff") Assert.Contains("Worker.Before", output);
                if (format != "json") continue;
                using var document = JsonDocument.Parse(output);
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
