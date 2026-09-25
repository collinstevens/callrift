using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DelegateCopyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DelegateValuesConstructWithoutInventedCallbacks(bool workspace, bool factory)
    {
        var operand = factory ? "Factory(action)" : "action";
        var files = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public delegate int Copied(); public static class Flow { public static int Run(System.Func<int> action) { var copy = new Copied(" + operand + "); return copy(); } static System.Func<int> Factory(System.Func<int> action) => action; }"
        };
        var after = new Dictionary<string, string>(files) { ["Unused.cs"] = "class Unused {}" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("delegate-copy", "Copying a delegate value binds its constructor without inventing a callback body; factory evaluation stays ordered before construction.", files, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", "--externals", .. mode, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.DoesNotContain("unresolved-call", output);
            Assert.DoesNotContain("? new Copied", output);
            Assert.Contains("new Copied", output);
            if (format != "json") continue;
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
            var calls = root.GetProperty("children").EnumerateArray().ToArray();
            var constructor = Assert.Single(calls, node => node.GetProperty("label").GetString() == "new Copied");
            Assert.Equal("resolved", constructor.GetProperty("after").GetProperty("binding").GetString());
            Assert.Empty(constructor.GetProperty("children").EnumerateArray());
            Assert.Equal(factory ? "Flow.Factory" : "new Copied", calls[0].GetProperty("label").GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompatibleDelegateValueRemainsUnresolved(bool workspace)
    {
        var files = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public delegate int Copied(); public static class Flow { public static Copied Run(System.Func<string> wrong) => new Copied(wrong); }"
        };
        var after = new Dictionary<string, string>(files) { ["Unused.cs"] = "class Unused {}" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("invalid-delegate-copy", "A delegate with an incompatible return type remains unresolved.", files, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", .. mode, "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        Assert.Contains("unresolved-call", output);
        Assert.Contains("? new Copied", output);
    }
}
