using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverConstraintTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferenceCastKeepsReceiverConstraint(bool workspace)
    {
        const string source = """
            interface IService { void Run(); }
            class Actual : IService { public void Run() { Before(); } void Before() {} void After() {} }
            class Adapter : IService { readonly Actual inner = new(); public void Run() => ((IService)inner).Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] entry = command == "diff" ? [] : ["--entry", "Adapter.Run"];
            string[] target = command == "reach" ? ["--to", "Actual.After"] : [];
            var output = await fixture.RunAsync([command, .. revisions, .. entry, .. target, .. mode, "--format", "json"]);
            using var document = Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Adapter.Run", root.GetProperty("label").GetString());
            var dispatch = root.GetProperty("children")[0];
            Assert.EndsWith("::Actual.Run()", Assert.Single(dispatch.GetProperty("after").GetProperty("targetIds").EnumerateArray()).GetString());
            Assert.Contains("Actual.After", output);
            Assert.DoesNotContain("cycle", output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaticReceiverExcludesSiblingOverrides(bool workspace)
    {
        const string source = """
            class Worker { public virtual void Run() {} }
            class Left : Worker {}
            class Right : Worker { public override void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Handle(Left worker) => worker.Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(output);
        Assert.Equal("Right.Run", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        var tree = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Handle", .. mode, "--format", "json"]);
        using var treeDocument = Parse(tree);
        Assert.Empty(treeDocument.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.DoesNotContain("Right", tree);
        Assert.Contains("Worker.Run", tree);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserConversionDoesNotConstrainTheResultToItsInput(bool workspace)
    {
        const string source = """
            interface IService { void Run(); }
            class Original : IService { public void Run() {} public static explicit operator Actual(Original value) => new Actual(); }
            class Actual : IService { public void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(Original source) => ((IService)(Actual)source).Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
        Assert.Contains("Actual.After", output);
        Assert.DoesNotContain("Original.Run", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContractAndReceiverShareGenericSubstitutions(bool workspace)
    {
        const string source = """
            interface IConsumer<T> { void Consume(T value); }
            class Box<T> : IConsumer<T> { public void Consume(T value) { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(Box<string> box) => ((IConsumer<int>)(object)box).Consume(1); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Box<T>.Consume", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        Assert.DoesNotContain("Flow.Run", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TupleArgumentsRemainValidReceiverConstraints(bool workspace)
    {
        const string source = """
            abstract class Worker<T> { public abstract void Run(); }
            class Actual : Worker<(string, int)> { public override void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(Worker<System.ValueTuple<string, int>> worker) => worker.Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Flow.Run", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        Assert.Contains("Actual.After", output);
    }

    private static Task<GitFixture> CreateAsync(string before, string after)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        return GitFixture.CreateAsync(new Scenario("receiver-constraints", "Static receiver types constrain possible dispatch through ordinary reference casts.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = before },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []));
    }

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }
}
