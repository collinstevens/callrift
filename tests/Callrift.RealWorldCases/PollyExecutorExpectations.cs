using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyExecutorExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (!focused)
        {
            Assert.Equal(workspace ? 23 : 9, result.Trees.Count);
            if (workspace)
            {
                var wrappers = result.Trees.Where(node => node.Label.StartsWith("ResiliencePipeline<T>.", StringComparison.Ordinal)).ToArray();
                Assert.Equal(11, wrappers.Length);
                Assert.All(wrappers, node => Assert.Equal("ResiliencePipeline<T>.get_Pipeline", node.Children[0].Label));
            }
            else
            {
                var benchmark = Assert.Single(result.Trees, node => node.Label == "MultipleStrategiesBenchmark.ExecuteStrategyPipeline_NonGeneric_V8");
                Assert.Equal(["ResilienceContextPool.get_Shared", "ResilienceContextPool.Get"], benchmark.Children.Take(2).Select(node => node.Label));
                Assert.Equal("ResilienceContextPool.get_Shared", benchmark.Children[^2].Label);
            }
            return;
        }

        var root = Assert.Single(result.Trees);
        Assert.Equal("ScheduledTaskExecutor.ScheduleTask", root.Label);
        Assert.Equal(workspace ? "ObjectDisposedException.ThrowIf" : "if (_disposed)", root.Children[0].Label);
        var removed = Assert.Single(root.Children, node => node.Mark == '-');
        var added = Assert.Single(root.Children, node => node.Mark == '+');
        Assert.EndsWith(".TaskCompletionSource<TResult>..ctor()", removed.Before!.SymbolId);
        Assert.EndsWith(".TaskCompletionSource<TResult>..ctor(global::System.Threading.Tasks.TaskCreationOptions)", added.After!.SymbolId);
        Assert.Equal(["_semaphore.Release", "TaskCompletionSource<TResult>.get_Task"], root.Children.TakeLast(2).Select(node => node.Label));
        var getter = root.Children[^1];
        Assert.Equal(' ', getter.Mark);
        Assert.Equal(getter.Before!.SymbolId, getter.After!.SymbolId);
        Assert.Equal(33, Assert.Single(getter.After.CallSites).Line);
    }
}
