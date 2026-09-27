using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AspNetGuardExpectations
{
    public static void VerifyDebugView(DiffResult result)
    {
        const string contract = "source::Microsoft.AspNetCore.Routing.EndpointDataSource.get_Endpoints()";
        const string implementation = "source::Microsoft.AspNetCore.Routing.CompositeEndpointDataSource.get_Endpoints()";
        var root = Assert.Single(result.Trees, node => node.Label == "WebApplication.WebApplicationDebugView.get_Endpoints");
        var guard = Assert.Single(root.Children, node => node.Label == "if (dataSource is CompositeEndpointDataSource compositeEndpointDataSource)");
        var registered = Assert.Single(guard.Children, node => node.Label.StartsWith("if (compositeEndpointDataSource.DataSources.Intersect", StringComparison.Ordinal));
        var narrowed = Assert.Single(registered.Children);
        Assert.All(new[] { narrowed.Before!, narrowed.After! }, side =>
        {
            Assert.Equal(contract, side.SymbolId);
            Assert.Equal("possible", side.Dispatch);
            Assert.Equal([implementation], side.TargetIds);
            Assert.Equal(283, Assert.Single(side.CallSites).Line);
        });
        var unguarded = Assert.Single(root.Children, node => node.Before?.SymbolId == contract);
        Assert.All(new[] { unguarded.Before!, unguarded.After! }, side =>
        {
            Assert.Equal("possible", side.Dispatch);
            Assert.Equal(8, side.TargetIds.Count);
            Assert.Contains(implementation, side.TargetIds);
            Assert.Equal(292, Assert.Single(side.CallSites).Line);
        });
        Assert.Empty(narrowed.Children);
        Assert.Equal(9, unguarded.Children.Count);
    }
}
