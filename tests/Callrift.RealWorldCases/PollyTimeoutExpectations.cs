using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyTimeoutExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("generic-context-limit", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        Assert.Equal(1878, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(15, result.Diagnostics.Count(diagnostic => diagnostic.Code == "generic-context-limit"));
        Assert.Equal(6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "unresolved-static-initializer");
        if (!focused)
        {
            Assert.Equal(74, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "AsyncPolicy.ExecuteAndCaptureAsync");
            Assert.Contains(result.Trees, node => node.Label == "Policy<TResult>.ExecuteAndCapture");
            Assert.Contains(result.Trees, node => node.Label == "Timeout.Timeout_Synchronous_With_Result_Succeeds");
            Assert.Contains(result.Trees, node => node.Label == "BridgeBenchmark.NoOpAsync");
            return;
        }
        Assert.Equal(2, result.Trees.Count);
        foreach (var root in result.Trees)
        {
            var asynchronous = root.Label == "AsyncTimeoutEngine.ImplementationAsync";
            Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
            Assert.Equal(root.Before.Signature, root.After.Signature);
            var handler = Assert.Single(root.Children, node => node.Label == "catch (Exception ex)");
            var guard = Assert.Single(handler.Children);
            Assert.Equal("if (ex is OperationCanceledException && timeoutCancellationTokenSource.IsCancellationRequested)", guard.Label);
            Assert.Equal(asynchronous ? 3 : 2, guard.Children.Count);
            Assert.Equal(asynchronous ? "onTimeoutAsync" : "onTimeout", guard.Children[0].Label);
            var exception = guard.Children[^1];
            Assert.Equal("new TimeoutRejectedException", exception.Label);
            Assert.Equal('~', exception.Mark);
            Assert.Equal("signature changed", exception.Detail);
            Assert.EndsWith("TimeoutRejectedException..ctor(string,global::System.Exception)", exception.Before!.SymbolId);
            Assert.EndsWith("TimeoutRejectedException..ctor(string,global::System.TimeSpan,global::System.Exception)", exception.After!.SymbolId);
            Assert.Contains("System.TimeSpan timeout", exception.After.Signature);
            Assert.Equal("src/Polly.Core/Timeout/TimeoutRejectedException.cs", exception.After.Definition!.Path);
            Assert.Equal(37, exception.Before.Definition!.Line);
            Assert.Equal(63, exception.After.Definition.Line);
            Assert.Equal(asynchronous ? 49 : 65, Assert.Single(exception.After.CallSites).Line);
            var baseConstructor = Assert.Single(exception.Children);
            Assert.Equal("new ExecutionRejectedException", baseConstructor.Label);
            Assert.Equal(baseConstructor.Before!.SymbolId, baseConstructor.After!.SymbolId);
            Assert.Equal("depth-limit", baseConstructor.Omission!.Reason);
            Assert.All(Descendants(root.Children).Where(node => node != exception), node => Assert.Equal(' ', node.Mark));
            Assert.DoesNotContain(Descendants(root.Children), node => node.Label.Contains("set_Timeout", StringComparison.Ordinal)
                || node.Label == "combinedTokenSource.Dispose" || node.Label == "timeoutCancellationTokenSource.Dispose");
            if (asynchronous)
            {
                var cleanup = Assert.Single(root.Children, node => node.Label == "finally");
                var cancel = Assert.Single(Assert.Single(cleanup.Children).Children);
                Assert.Equal("combinedTokenSource.Cancel", cancel.Label);
                Assert.DoesNotContain(Descendants(root.Children), node => node.Label.Contains("CancelAsync", StringComparison.Ordinal));
            }
        }
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
