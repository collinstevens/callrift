using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyRetryExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("generic-context-limit", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        Assert.Equal(workspace ? 15 : 16, result.Diagnostics.Count(diagnostic => diagnostic.Code == "generic-context-limit"));
        Assert.Equal(workspace ? 0 : 1896, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(workspace ? 0 : 1, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        Assert.Equal(workspace ? 0 : 6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        if (!focused)
        {
            Assert.Equal(workspace ? 21 : 8, result.Trees.Count);
            Assert.Equal(workspace, result.Trees.Any(node => node.Label == "ResiliencePipeline.Execute"));
            Assert.Equal(!workspace, result.Trees.Any(node => node.Label == "CompositeComponentBenchmark.CompositeComponent_ExecuteCore"));
            Assert.Contains(Descendants(result.Trees), node => node.Label == "⇢ BridgeComponent<T>.ExecuteCore" && node.Mark == '~');
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("RetryResilienceStrategy<T>.ExecuteCore", root.Label);
        Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
        Assert.Equal(root.Before.Signature, root.After.Signature);
        var loop = Assert.Single(root.Children);
        Assert.Equal("while (true)", loop.Label);
        Assert.DoesNotContain(loop.Children, node => node.Label.Contains("!handle", StringComparison.Ordinal));
        Assert.DoesNotContain(loop.Children, node => node.Label == "if (incrementAttempts)");
        Assert.Equal(["ResilienceContext.get_CancellationToken", "CancellationToken.get_IsCancellationRequested"],
            loop.Children.Where(node => node.Mark == '-' && node.Label.Contains("get_", StringComparison.Ordinal)).Select(node => node.Label));
        var retryCallback = Assert.Single(loop.Children, node => node.Label == "if (OnRetry is not null)");
        var disposal = Assert.Single(loop.Children, node => node.Label == "if (outcome.TryGetResult(out var resultValue))");
        var previousDelay = Assert.Single(loop.Children, node => node.Label == "if (delay > TimeSpan.Zero)" && node.Mark == '-');
        Assert.Equal(["try", "catch (OperationCanceledException e)"], previousDelay.Children.Select(node => node.Label));
        var nextTry = Assert.Single(loop.Children, node => node.Label == "try" && node.Mark == '+');
        Assert.Equal(["ResilienceContext.get_CancellationToken", "context.CancellationToken.ThrowIfCancellationRequested", "TimeSpan.op_GreaterThan", "if (delay > TimeSpan.Zero)"],
            nextTry.Children.Select(node => node.Label));
        var order = loop.Children.ToList();
        Assert.True(order.IndexOf(retryCallback) < order.IndexOf(disposal));
        Assert.True(order.IndexOf(disposal) < order.IndexOf(nextTry));
        var cancellation = nextTry.Children[1];
        Assert.Equal("metadata", cancellation.After!.Origin);
        Assert.EndsWith("System.Threading.CancellationToken.ThrowIfCancellationRequested()", cancellation.After.SymbolId);
        Assert.Equal(105, Assert.Single(cancellation.After.CallSites).Line);
        var previousCalls = previousDelay.Children[0].Children;
        var nextCalls = nextTry.Children[3].Children;
        Assert.Equal(3, previousCalls.Count);
        Assert.Equal("ResilienceContext.get_ContinueOnCapturedContext", nextCalls[1].Label);
        Assert.Equal(previousCalls.Select(node => node.Label), nextCalls.Select(node => node.Label));
        for (var index = 0; index < previousCalls.Count; index++)
        {
            Assert.Equal(previousCalls[index].Before!.SymbolId, nextCalls[index].After!.SymbolId);
            Assert.Equal(108, Assert.Single(previousCalls[index].Before!.CallSites).Line);
            Assert.Equal(110, Assert.Single(nextCalls[index].After!.CallSites).Line);
            Assert.Equal(workspace || index == 1 ? "resolved" : "unresolved", nextCalls[index].After!.Binding);
        }
        var nextCatch = Assert.Single(loop.Children, node => node.Label == "catch (OperationCanceledException e)" && node.Mark == '+');
        Assert.True(order.IndexOf(nextTry) < order.IndexOf(nextCatch));
        var previousOutcome = Assert.Single(previousDelay.Children[1].Children);
        var nextOutcome = Assert.Single(nextCatch.Children);
        Assert.Equal("Outcome.FromException", nextOutcome.Label);
        Assert.Equal(previousOutcome.Before!.SymbolId, nextOutcome.After!.SymbolId);
        Assert.Equal(112, Assert.Single(previousOutcome.Before.CallSites).Line);
        Assert.Equal(116, Assert.Single(nextOutcome.After.CallSites).Line);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
