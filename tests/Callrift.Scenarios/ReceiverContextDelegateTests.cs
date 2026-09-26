using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverContextDelegateTests
{
    [Fact]
    [Trait("Layer", "Fast")]
    public Task DelegateFieldDoesNotSplitInheritedImplementation() => VerifyDelegateFieldDoesNotSplitInheritedImplementation(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDelegateFieldDoesNotSplitInheritedImplementation() => VerifyDelegateFieldDoesNotSplitInheritedImplementation(true);

    private static async Task VerifyDelegateFieldDoesNotSplitInheritedImplementation(bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        const string source = "interface IWorker { void Run(); } abstract class Base : IWorker { private System.Action callback = () => {}; public void Run() => callback(); } sealed class Left : Base {} sealed class Right : Base {} static class Entry { public static void Run(IWorker worker) => worker.Run(); }";
        var before = new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source };
        var after = new Dictionary<string, string>(before) { ["marker.txt"] = "after" };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("receiver-delegate-field", "A separate delegate receiver does not split identical inherited implementations.", before, after, []), workspace);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 16 }, before: true);
        using var tree = JsonDocument.Parse(output);
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        var implementation = tree.RootElement.GetProperty("trees")[0].GetProperty("children")[0];
        Assert.Equal("IWorker.Run → Base.Run", implementation.GetProperty("label").GetString());
        Assert.Empty(implementation.GetProperty("children").EnumerateArray());
    }
}
