using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverContextDelegateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelegateFieldDoesNotSplitInheritedImplementation(bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        const string source = "interface IWorker { void Run(); } abstract class Base : IWorker { private System.Action callback = () => {}; public void Run() => callback(); } sealed class Left : Base {} sealed class Right : Base {} static class Entry { public static void Run(IWorker worker) => worker.Run(); }";
        var before = new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source };
        var after = new Dictionary<string, string>(before) { ["marker.txt"] = "after" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("receiver-delegate-field", "A separate delegate receiver does not split identical inherited implementations.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["tree", fixture.Before, "--entry", "Entry.Run", "--depth", "16", .. mode, "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var tree = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        var implementation = tree.RootElement.GetProperty("trees")[0].GetProperty("children")[0];
        Assert.Equal("IWorker.Run → Base.Run", implementation.GetProperty("label").GetString());
        Assert.Empty(implementation.GetProperty("children").EnumerateArray());
    }
}
