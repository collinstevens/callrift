using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AspNetStreamCtsExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        Assert.Equal(22977, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(179, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
        if (!focused)
        {
            AspNetGuardExpectations.VerifyDebugView(result);
            Assert.Equal(2325, result.Trees.Count);
            return;
        }

        var root = Assert.Single(result.Trees);
        Assert.Equal("DefaultHubDispatcher<THub>.StreamAsync", root.Label);
        Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
        Assert.Equal(root.Before.Signature, root.After.Signature);
        var creation = Assert.Single(root.Children, node => node.Label == "if (streamCts is null)");
        Assert.Equal(["HubConnectionContext.get_ConnectionAborted", "CancellationTokenSource.CreateLinkedTokenSource"],
            creation.Children.Select(node => node.Label));
        var invocation = Assert.Single(root.Children, node => node.Label == "try");
        Assert.Equal("HubConnectionContext.get_ActiveRequestCancellationSources", invocation.Children[0].Label);
        Assert.Equal("connection.ActiveRequestCancellationSources.TryAdd", invocation.Children[1].Label);
        var rejected = invocation.Children[2];
        Assert.Equal("if (!connection.ActiveRequestCancellationSources.TryAdd(invocationId, streamCts))", rejected.Label);
        Assert.Equal(["DefaultHubDispatcherLog.InvocationIdInUse", "streamCts.Dispose"], rejected.Children.Select(node => node.Label));
        Assert.Equal(' ', rejected.Children[0].Mark);
        var added = Assert.Single(Descendants(result.Trees), node => node.Mark == '+');
        Assert.Same(rejected.Children[1], added);
        Assert.Null(added.Before);
        Assert.Equal("source::System.Threading.CancellationTokenSource.Dispose()", added.After!.SymbolId);
        Assert.Equal("metadata", added.After.Origin);
        Assert.Equal(612, Assert.Single(added.After.CallSites).Line);
        Assert.DoesNotContain(Descendants(result.Trees), node => node.Mark == '-');
        var enumeration = Assert.Single(invocation.Children, node => node.Label == "while (await enumerator.MoveNextAsync())");
        Assert.Equal(["enumerator.MoveNextAsync", "IAsyncEnumerator<T>.get_Current", "StreamItemMessage.set_Item", "HubConnectionContext.WriteAsync"],
            enumeration.Children.Select(node => node.Label));
        var cleanup = Assert.Single(root.Children, node => node.Label == "finally");
        var ownership = Assert.Single(cleanup.Children, node => node.Label == "if (ctsRegistered)");
        Assert.Equal(["HubConnectionContext.get_ActiveRequestCancellationSources", "connection.ActiveRequestCancellationSources.TryRemove", "streamCts.Dispose"],
            ownership.Children.Select(node => node.Label));
        Assert.All(ownership.Children, node => Assert.Equal(' ', node.Mark));
        var retained = ownership.Children[^1];
        Assert.Equal(added.After.SymbolId, retained.Before!.SymbolId);
        Assert.Equal(retained.Before.SymbolId, retained.After!.SymbolId);
        Assert.Equal(684, Assert.Single(retained.Before.CallSites).Line);
        Assert.Equal(685, Assert.Single(retained.After.CallSites).Line);
        Assert.Contains(cleanup.Children, node => node.Label == "DefaultHubDispatcher<THub>.CleanupInvocation");
        Assert.Contains(cleanup.Children, node => node.Label == "CompletionMessage.WithError");
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
