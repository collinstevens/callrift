using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DelegateCopyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task DelegateValuesConstructWithoutInventedCallbacks(bool factory) => VerifyDelegateValuesConstructWithoutInventedCallbacks(false, factory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDelegateValuesConstructWithoutInventedCallbacks(bool factory) => VerifyDelegateValuesConstructWithoutInventedCallbacks(true, factory);

    private static async Task VerifyDelegateValuesConstructWithoutInventedCallbacks(bool workspace, bool factory)
    {
        var operand = factory ? "Factory(action)" : "action";
        var files = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public delegate int Copied(); public static class Flow { public static int Run(System.Func<int> action) { var copy = new Copied(" + operand + "); return copy(); } static System.Func<int> Factory(System.Func<int> action) => action; }"
        };
        var after = new Dictionary<string, string>(files) { ["Unused.cs"] = "class Unused {}" };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("delegate-copy", "Copying a delegate value binds its constructor without inventing a callback body; factory evaluation stays ordered before construction.", files, after, []), workspace);
        var outputs = await fixture.QueryFormatsAsync(new DiffOptions { Entries = ["Flow.Run"], IncludeExternals = true });
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = outputs[format];
            Assert.DoesNotContain("unresolved-call", output);
            Assert.DoesNotContain("? new Copied", output);
            Assert.Contains("new Copied", output);
            if (format != "json") continue;
            using var document = JsonDocument.Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
            var calls = root.GetProperty("children").EnumerateArray().ToArray();
            var constructor = Assert.Single(calls, node => node.GetProperty("label").GetString() == "new Copied");
            Assert.Equal("resolved", constructor.GetProperty("after").GetProperty("binding").GetString());
            Assert.Empty(constructor.GetProperty("children").EnumerateArray());
            Assert.Equal(factory ? "Flow.Factory" : "new Copied", calls[0].GetProperty("label").GetString());
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task IncompatibleDelegateValueRemainsUnresolved() => VerifyIncompatibleDelegateValueRemainsUnresolved(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceIncompatibleDelegateValueRemainsUnresolved() => VerifyIncompatibleDelegateValueRemainsUnresolved(true);

    private static async Task VerifyIncompatibleDelegateValueRemainsUnresolved(bool workspace)
    {
        var files = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public delegate int Copied(); public static class Flow { public static Copied Run(System.Func<string> wrong) => new Copied(wrong); }"
        };
        var after = new Dictionary<string, string>(files) { ["Unused.cs"] = "class Unused {}" };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("invalid-delegate-copy", "A delegate with an incompatible return type remains unresolved.", files, after, []), workspace);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] });
        Assert.Contains("unresolved-call", output);
        Assert.Contains("? new Copied", output);
    }
}
