using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyFaultExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("callbacks-are-possible-calls", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        Assert.Equal(workspace ? 14 : 15, result.Diagnostics.Count(diagnostic => diagnostic.Code == "generic-context-limit"));
        if (workspace) Assert.All(result.Diagnostics, diagnostic => Assert.Equal("generic-context-limit", diagnostic.Code));
        if (!focused && !workspace)
        {
            Assert.Equal([
                "Chaos.AddMyChaos", "Chaos.ApplyChaosSelectively", "Chaos.ApplyChaosSelectivelyWithChaosManager",
                "Chaos.CentralPipeline", "Chaos.FaultGenerator", "Chaos.FaultUsage", "Chaos.Pattern_OnlyInjectFault",
                "samples/Chaos/Program.cs::<top-level>"
            ], result.Trees.Select(node => node.Label));
            var conversions = Descendants(result.Trees).Where(node => node.Label == "FaultGenerator.op_Implicit").ToArray();
            Assert.Equal(8, conversions.Length);
            Assert.All(conversions, node =>
            {
                Assert.Equal('~', node.Mark);
                Assert.Equal("changes below depth limit", node.Detail);
                AssertConversion(node);
            });
            var creation = Assert.Single(result.Trees[0].Children, node => node.Label == "new Chaos.MyChaosOptions");
            Assert.Equal('~', creation.Mark);
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("FaultGenerator.op_Implicit", root.Label);
        AssertConversion(root);
        Assert.Equal(' ', root.Mark);
        Assert.Null(root.Detail);
        var callback = root.Children[2];
        Assert.Equal("callback", callback.After!.Relation);
        var nullable = Assert.Single(callback.Children, node => node.Label == "if (generatorDelegate(args.Context) is not null)");
        Assert.Equal('+', nullable.Mark);
        var exception = Assert.Single(nullable.Children);
        Assert.Equal("Outcome<TResult>.get_Exception", exception.Label);
        Assert.Equal('+', exception.Mark);
        Assert.Null(exception.Before);
        var removedException = Assert.Single(callback.Children, node => node.Label == "Outcome<TResult>.get_Exception");
        Assert.Equal('-', removedException.Mark);
        Assert.Equal(removedException.Before!.SymbolId, exception.After!.SymbolId);
        if (!focused) return;
        Assert.Equal([workspace ? "Guard.NotNull" : "? Guard.NotNull", "GeneratorHelper<TResult>.CreateGenerator", "callback"],
            root.Children.Select(node => node.Label));
        var guard = root.Children[0];
        Assert.Equal(workspace ? "resolved" : "unresolved", guard.After!.Binding);
        if (workspace)
            Assert.EndsWith("::System.ArgumentNullException.ThrowIfNull(object,string)", Assert.Single(guard.Children).After!.SymbolId);
        var helper = root.Children[1];
        Assert.Equal(["List<T>.get_Count", "_factories.ToArray", "_weights.ToArray", "callback"], helper.Children.Select(node => node.Label));
        var generated = helper.Children[3];
        Assert.Equal(["generator", "for (i < factories.Length)"], generated.Children.Select(node => node.Label));
        Assert.Equal("Array.get_Length", generated.Children[1].Children[0].Label);
        var condition = generated.Children[1].Children[1];
        Assert.Equal("if (generatedWeight < weight)", condition.Label);
        Assert.Equal("factories[i]", Assert.Single(condition.Children).Label);
        Assert.Equal(["FaultGeneratorArguments.get_Context", "generatorDelegate", "Nullable<T>.get_Value",
            "Outcome<TResult>.get_Exception", "if (generatorDelegate(args.Context) is not null)", "new ValueTask<Exception?>"],
            callback.Children.Select(node => node.Label));
        Assert.All(callback.Children, node => Assert.Equal(81, Assert.Single((node.After ?? node.Before)!.CallSites).Line));
        Assert.Equal('-', callback.Children[2].Mark);
        Assert.Null(callback.Children[2].After);
        Assert.All(Descendants(helper.Children), node => Assert.Equal(' ', node.Mark));
        Assert.Equal(["Outcome<TResult>.get_ExceptionDispatchInfo", "if (ExceptionDispatchInfo is not null)"], exception.Children.Select(node => node.Label));
        Assert.Equal("ExceptionDispatchInfo.get_SourceException", Assert.Single(exception.Children[1].Children).Label);
    }

    private static void AssertConversion(DiffNode node)
    {
        Assert.Equal(node.Before!.SymbolId, node.After!.SymbolId);
        Assert.Equal(node.Before.Signature, node.After.Signature);
        Assert.Contains(".op_Implicit(global::Polly.Simmy.Fault.FaultGenerator)->global::System.Func<", node.After.SymbolId);
        Assert.Contains("System.Threading.Tasks.ValueTask<System.Exception?>", node.After.Signature);
        Assert.Equal("src/Polly.Core/Simmy/Fault/FaultGenerator.cs", node.After.Definition!.Path);
        Assert.Equal(74, node.After.Definition.Line);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
