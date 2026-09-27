using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogRestrictedListenerExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (result.Coverage.Mode == "msbuild")
            Assert.Empty(result.Diagnostics);
        else
            Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        if (!focused)
        {
            Assert.Equal(["LoggerSinkConfiguration.FallbackChain", "LoggerSinkConfiguration.Fallible"], result.Trees.Select(node => node.Label));
            Assert.All(result.Trees, root => Assert.Contains(Descendants(root.Children), node =>
                node.Label == "new FailureListenerSink" && node.Detail == "changes below depth limit"));
            return;
        }

        Assert.Equal(["new FailureListenerSink", "RestrictedSink.SetFailureListener"], result.Trees.Select(node => node.Label));
        var constructor = result.Trees[0];
        Assert.Equal(' ', constructor.Mark);
        var guard = Assert.Single(constructor.Children, node => node.Kind == "branch");
        Assert.Equal("if (inner is ISetLoggingFailureListener sfl)", guard.Label);
        var invocation = Assert.Single(guard.Children);
        Assert.Equal("possible", invocation.After!.Dispatch);
        Assert.Equal(2, invocation.Before!.TargetIds.Count);
        Assert.Equal(3, invocation.After.TargetIds.Count);
        var addedTarget = Assert.Single(invocation.Children, node => node.Mark == '+');
        Assert.Equal("⇢ RestrictedSink.SetFailureListener", addedTarget.Label);
        Assert.Null(addedTarget.Before);
        var addedRoot = result.Trees[1];
        Assert.Equal('+', addedRoot.Mark);
        Assert.Null(addedRoot.Before);
        Assert.Equal(addedRoot.After!.SymbolId, addedTarget.After!.SymbolId);
        Assert.Equal("public Serilog.Core.Sinks.RestrictedSink.SetFailureListener(Serilog.Core.ILoggingFailureListener failureListener) -> void", addedRoot.After.Signature);
        var forwardingGuard = Assert.Single(addedRoot.Children);
        Assert.Equal("if (_sink is ISetLoggingFailureListener sfl)", forwardingGuard.Label);
        var forwarded = Assert.Single(forwardingGuard.Children);
        Assert.Equal(45, Assert.Single(forwarded.After!.CallSites).Line);
        Assert.Equal("possible", forwarded.After.Dispatch);
        Assert.Equal(3, forwarded.After.TargetIds.Count);
        var recursive = Assert.Single(forwarded.Children, node => node.Label == "⇢ RestrictedSink.SetFailureListener");
        Assert.Equal("cycle", recursive.Omission!.Reason);
        Assert.Equal(addedRoot.After.SymbolId, recursive.Omission.SymbolId);
        Assert.All(Descendants(result.Trees).Where(node => node.Label == "⇢ RestrictedSink.SetFailureListener"), node =>
        {
            Assert.Equal('+', node.Mark);
            Assert.Null(node.Before);
        });
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
