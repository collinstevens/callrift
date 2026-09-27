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
        Assert.Equal(1883, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
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
            Assert.Equal(2, handler.Children.Count);
            var typeGuard = handler.Children[0];
            Assert.Equal("if (ex is OperationCanceledException)", typeGuard.Label);
            Assert.Equal("CancellationTokenSource.get_IsCancellationRequested", Assert.Single(typeGuard.Children).Label);
            var guard = handler.Children[1];
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
            Assert.Equal(2, exception.Children.Count);
            var baseConstructor = exception.Children[0];
            Assert.Equal("new ExecutionRejectedException", baseConstructor.Label);
            Assert.Equal(baseConstructor.Before!.SymbolId, baseConstructor.After!.SymbolId);
            Assert.Equal("depth-limit", baseConstructor.Omission!.Reason);
            var setter = exception.Children[1];
            Assert.Equal("TimeoutRejectedException.set_Timeout", setter.Label);
            Assert.Equal('+', setter.Mark);
            Assert.Null(setter.Before);
            Assert.Equal("resolved", setter.After!.Binding);
            Assert.Contains("private Polly.Timeout.TimeoutRejectedException.set_Timeout(System.TimeSpan value)", setter.After.Signature);
            Assert.Equal(69, setter.After.Definition!.Line);
            Assert.Equal(64, Assert.Single(setter.After.CallSites).Line);
            Assert.All(Descendants(root.Children).Where(node => node != exception && node != setter), node => Assert.Equal(' ', node.Mark));
            Assert.DoesNotContain(Descendants(root.Children), node => node.Label == "combinedTokenSource.Dispose" || node.Label == "timeoutCancellationTokenSource.Dispose");
            if (asynchronous)
            {
                var cleanup = Assert.Single(root.Children, node => node.Label == "finally");
                Assert.Equal(3, cleanup.Children.Count);
                Assert.Equal("CancellationTokenSource.get_IsCancellationRequested", cleanup.Children[0].Label);
                Assert.Equal("if (!combinedTokenSource.IsCancellationRequested)", cleanup.Children[1].Label);
                Assert.Equal("CancellationTokenSource.get_IsCancellationRequested", Assert.Single(cleanup.Children[1].Children).Label);
                Assert.Equal("if (!combinedTokenSource.IsCancellationRequested && timeoutCancellationTokenSource.IsCancellation…", cleanup.Children[2].Label);
                var cancel = Assert.Single(cleanup.Children[2].Children);
                Assert.Equal("combinedTokenSource.Cancel", cancel.Label);
                Assert.DoesNotContain(Descendants(root.Children), node => node.Label.Contains("CancelAsync", StringComparison.Ordinal));
            }
        }
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
