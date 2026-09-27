using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class CleanArchitecturePreviousPageExpectations
{
    public static void Verify(DiffResult result)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.False(result.Truncated);
        Assert.Equal(356, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        var getter = Assert.Single(result.Trees);
        Assert.Equal("PaginatedList<T>.get_HasPreviousPage", getter.Label);
        Assert.Equal(getter.Before!.SymbolId, getter.After!.SymbolId);
        Assert.Equal(getter.Before.Signature, getter.After.Signature);
        Assert.Equal(18, getter.Before.Definition!.Line);
        Assert.Equal(18, getter.After.Definition!.Line);
        Assert.Equal(["PaginatedList<T>.get_PageNumber", "if (PageNumber > 1)"], getter.Children.Select(node => node.Label));
        var firstRead = getter.Children[0];
        Assert.Equal(' ', firstRead.Mark);
        Assert.Equal(36, Assert.Single(firstRead.After!.CallSites).Column);
        var guard = getter.Children[1];
        Assert.Equal('+', guard.Mark);
        Assert.Null(guard.Before);
        Assert.Equal("branch", guard.Kind);
        Assert.Equal(["PaginatedList<T>.get_PageNumber", "PaginatedList<T>.get_TotalPages"], guard.Children.Select(node => node.Label));
        Assert.Equal([54, 68], guard.Children.Select(node => Assert.Single(node.After!.CallSites).Column));
        Assert.All(guard.Children, node =>
        {
            Assert.Equal('+', node.Mark);
            Assert.Null(node.Before);
            Assert.Equal("resolved", node.After!.Binding);
            Assert.Equal("direct", node.After.Dispatch);
            Assert.Equal(node.After.SymbolId, Assert.Single(node.After.TargetIds));
            Assert.Empty(node.Children);
            Assert.Null(node.Omission);
        });
    }
}
