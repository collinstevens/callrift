using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogContextCollectionExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(focused ? 3 : 2, result.Trees.Count);

        var array = Assert.Single(result.Trees, node => node.Before is not null);
        Assert.Equal(array.Before!.SymbolId, array.After!.SymbolId);
        Assert.Contains("params Serilog.Core.ILogEventEnricher[] enrichers", array.After.Signature);
        Assert.Equal(["Guard.AgainstNull", "enrichers.AsSpan", "LogContext.Push"],
            array.Children.Where(node => node.After is not null).Select(node => node.Label));
        Assert.Equal(' ', array.Children[0].Mark);
        Assert.Contains(array.Children, node => node.Label == "for (i < enrichers.Length)" && node.Mark == '-');
        var forwarding = array.Children[^1];
        Assert.Equal('+', forwarding.Mark);
        Assert.Equal("direct", forwarding.After!.Dispatch);
        Assert.EndsWith("LogContext.Push(global::System.ReadOnlySpan<global::Serilog.Core.ILogEventEnricher>)", forwarding.After.SymbolId);
        Assert.Equal(97, Assert.Single(forwarding.After.CallSites).Line);
        VerifySpan(forwarding);

        var enumerable = Assert.Single(result.Trees, node => node.After!.Signature!.Contains("params System.Collections.Generic.IEnumerable<", StringComparison.Ordinal));
        Assert.Equal('+', enumerable.Mark);
        Assert.Equal(["Guard.AgainstNull", "LogContext.GetOrCreateEnricherStack", "new LogContext.ContextStackBookmark",
            "foreach (var enricher in enrichers)", "LogContext.set_Enrichers"], enumerable.Children.Select(node => node.Label));
        var guard = Assert.Single(enumerable.Children[0].Children);
        Assert.Equal("if (argument is null)", guard.Label);
        Assert.Equal("new ArgumentNullException", Assert.Single(guard.Children).Label);
        VerifyLoopAndSetter(enumerable);

        if (focused)
        {
            var span = Assert.Single(result.Trees, node => node.After!.SymbolId == forwarding.After.SymbolId);
            Assert.Equal('+', span.Mark);
            Assert.Contains("params System.ReadOnlySpan<", span.After!.Signature);
            VerifySpan(span);
            var stack = span.Children[0];
            Assert.Equal(["LogContext.get_Enrichers", "if (enrichers == null)"], stack.Children.Select(node => node.Label));
            Assert.Equal(["EnricherStack.get_Empty", "LogContext.set_Enrichers"], stack.Children[1].Children.Select(node => node.Label));
        }
    }

    private static void VerifySpan(DiffNode node)
    {
        Assert.Equal(["LogContext.GetOrCreateEnricherStack", "new LogContext.ContextStackBookmark",
            "foreach (var enricher in enrichers)", "LogContext.set_Enrichers"], node.Children.Select(child => child.Label));
        VerifyLoopAndSetter(node);
    }

    private static void VerifyLoopAndSetter(DiffNode node)
    {
        var loop = Assert.Single(node.Children, child => child.Label == "foreach (var enricher in enrichers)");
        var push = Assert.Single(loop.Children);
        Assert.Equal("EnricherStack.Push", push.Label);
        Assert.Equal("direct", push.After!.Dispatch);
        Assert.Equal("LogContext.set_Enrichers", node.Children[^1].Label);
        Assert.Contains(node.Children[^1].Children, child => child.Label == "AsyncLocal<T>.set_Value");
    }
}
