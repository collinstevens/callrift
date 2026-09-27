using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverConstraintTests
{
    [Fact]
    [Trait("Layer", "Fast")]
    public Task ReferenceCastKeepsReceiverConstraint() => VerifyReferenceCastKeepsReceiverConstraint(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceReferenceCastKeepsReceiverConstraint() => VerifyReferenceCastKeepsReceiverConstraint(true);

    private static async Task VerifyReferenceCastKeepsReceiverConstraint(bool workspace)
    {
        const string source = """
            interface IService { void Run(); }
            class Actual : IService { public void Run() { Before(); } void Before() {} void After() {} }
            class Adapter : IService { readonly Actual inner = new(); public void Run() => ((IService)inner).Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), workspace);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var options = new DiffOptions { Entries = command == "diff" ? [] : ["Adapter.Run"] };
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Actual.After" : null);
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

    [Fact]
    [Trait("Layer", "Fast")]
    public Task StaticReceiverExcludesSiblingOverrides() => VerifyStaticReceiverExcludesSiblingOverrides(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceStaticReceiverExcludesSiblingOverrides() => VerifyStaticReceiverExcludesSiblingOverrides(true);

    private static async Task VerifyStaticReceiverExcludesSiblingOverrides(bool workspace)
    {
        const string source = """
            class Worker { public virtual void Run() {} }
            class Left : Worker {}
            class Right : Worker { public override void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Handle(Left worker) => worker.Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), workspace);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Equal("Right.Run", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        var tree = await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Handle"] });
        using var treeDocument = Parse(tree);
        Assert.Empty(treeDocument.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.DoesNotContain("Right", tree);
        Assert.Contains("Worker.Run", tree);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task UserConversionDoesNotConstrainTheResultToItsInput() => VerifyUserConversionDoesNotConstrainTheResultToItsInput(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceUserConversionDoesNotConstrainTheResultToItsInput() => VerifyUserConversionDoesNotConstrainTheResultToItsInput(true);

    private static async Task VerifyUserConversionDoesNotConstrainTheResultToItsInput(bool workspace)
    {
        const string source = """
            interface IService { void Run(); }
            class Original : IService { public void Run() {} public static explicit operator Actual(Original value) => new Actual(); }
            class Actual : IService { public void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(Original source) => ((IService)(Actual)source).Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), workspace);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
        Assert.Contains("Actual.After", output);
        Assert.DoesNotContain("Original.Run", output);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task ContractAndReceiverShareGenericSubstitutions() => VerifyContractAndReceiverShareGenericSubstitutions(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceContractAndReceiverShareGenericSubstitutions() => VerifyContractAndReceiverShareGenericSubstitutions(true);

    private static async Task VerifyContractAndReceiverShareGenericSubstitutions(bool workspace)
    {
        const string source = """
            interface IConsumer<T> { void Consume(T value); }
            class Box<T> : IConsumer<T> { public void Consume(T value) { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(Box<string> box) => ((IConsumer<int>)(object)box).Consume(1); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), workspace);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Box<T>.Consume", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        Assert.DoesNotContain("Flow.Run", output);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task TupleArgumentsRemainValidReceiverConstraints() => VerifyTupleArgumentsRemainValidReceiverConstraints(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceTupleArgumentsRemainValidReceiverConstraints() => VerifyTupleArgumentsRemainValidReceiverConstraints(true);

    private static async Task VerifyTupleArgumentsRemainValidReceiverConstraints(bool workspace)
    {
        const string source = """
            abstract class Worker<T> { public abstract void Run(); }
            class Actual : Worker<(string, int)> { public override void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(Worker<System.ValueTuple<string, int>> worker) => worker.Run(); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), workspace);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Flow.Run", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        Assert.Contains("Actual.After", output);
    }

    private static Task<AnalysisFixture> CreateAsync(string before, string after, bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        return AnalysisFixture.CreateAsync(new Scenario("receiver-constraints", "Static receiver types constrain possible dispatch through ordinary reference casts.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = before },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []), workspace);
    }

    private static JsonDocument Parse(string output)
    {
        return JsonDocument.Parse(output);
    }
}
