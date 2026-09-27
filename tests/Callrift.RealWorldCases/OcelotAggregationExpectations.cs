using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotAggregationExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        if (workspace) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(1070, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        }
        if (!focused)
        {
            Assert.Equal(workspace ? 10 : 14, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "new AggregateRouteConfig" && node.Detail == "signature changed");
            Assert.Contains(result.Trees, node => node.Label == "MultiplexingMiddleware.Invoke");
            Assert.Equal(workspace ? 3 : 2, result.Trees.Count(node => node.Label == "OcelotMiddlewareExtensions.UseOcelot"));
            Assert.Equal(workspace ? 0 : 3, result.Trees.Count(node => node.After?.Definition?.Path == "testing/Steps/WebSocketsSteps.cs"));
            return;
        }
        Assert.Equal(7, result.Trees.Count);
        VerifyPlaceholderCopy(result, workspace);
        VerifyOmittedIndexerWrites(result);
        var invoke = Assert.Single(result.Trees, node => node.Label == "MultiplexingMiddleware.Invoke");
        Assert.Contains("httpContext", invoke.Before!.Signature);
        Assert.DoesNotContain("httpContext", invoke.After!.Signature);
        Assert.Contains("context", invoke.After.Signature);
        var mainRoute = Assert.Single(invoke.Children, node => node.Label == "MultiplexingMiddleware.ProcessMainRouteAsync");
        Assert.Contains("async", mainRoute.Before!.Signature);
        Assert.DoesNotContain("async", mainRoute.After!.Signature);
        Assert.EndsWith(" -> System.Threading.Tasks.Task", mainRoute.After.Signature);
        var keyedRoutes = Assert.Single(invoke.Children, node => node.Label == "MultiplexingMiddleware.ProcessRoutesWithRouteKeysAsync");
        var fallback = Assert.Single(Descendants(keyedRoutes.Children), node => node.Label == "else (!(matchAdvancedAgg != null))");
        Assert.Equal('+', fallback.Mark);
        Assert.Equal(["MultiplexingMiddleware.ProcessRouteAsync", "processing.Add"], fallback.Children.Select(node => node.Label));
        Assert.DoesNotContain(Descendants(invoke.Children), node => node.Label is "if (downstreamRoutes.Count == 0)" or "if (responsesContexts.Length == 0)" or "if (mainResponseContext == null)");
        var creator = Assert.Single(result.Trees, node => node.Label == "AggregatesCreator.Create");
        var setup = Assert.Single(Descendants(creator.Children), node => node.Label == "AggregatesCreator.SetUpAggregateRoute");
        Assert.Contains(setup.Children, node => node.Label == "aggregateRoute.RouteKeys.Select" && node.Mark == '-');
        var lookup = Assert.Single(setup.Children, node => node.Label == "foreach (var key in aggregateRoute.RouteKeys)");
        Assert.Equal('+', lookup.Mark);
        Assert.Equal(["allRoutes.FirstOrDefault", "applicableRoutes.Add"], lookup.Children.Select(node => node.Label));
        Assert.DoesNotContain(Descendants(setup.Children), node => node.Label == "if (route is null)");
    }

    private static void VerifyPlaceholderCopy(DiffResult result, bool workspace)
    {
        var aggregation = Assert.Single(result.Trees, node => node.Label == "MultiplexingMiddleware.ProcessRouteWithComplexAggregation");
        Assert.Contains("IEnumerable<", aggregation.Before!.Signature);
        Assert.Contains(" -> System.Collections.Generic.List<", aggregation.After!.Signature);
        var loop = Assert.Single(aggregation.Children, node => node.Label == "foreach (var value in values)");
        var copy = Assert.Single(loop.Children, node => node.Label == "new List<PlaceholderNameAndValue>");
        Assert.Equal('+', copy.Mark);
        Assert.EndsWith("::System.Collections.Generic.List<T>..ctor(global::System.Collections.Generic.IEnumerable<T>)", copy.After!.SymbolId);
        Assert.Equal(165, Assert.Single(copy.After.CallSites).Line);
        Assert.Equal('-', Assert.Single(loop.Children, node => node.Label == "tPnv.Add").Mark);
        var add = Assert.Single(loop.Children, node => node.Label == "List<T>.Add");
        Assert.Equal('+', add.Mark);
        Assert.Equal(167, Assert.Single(add.After!.CallSites).Line);
        Assert.Equal(' ', Assert.Single(loop.Children, node => node.Label == "MultiplexingMiddleware.ProcessRouteAsync").Mark);
        if (workspace)
        {
            var placeholder = Assert.Single(loop.Children, node => node.Label == "new PlaceholderNameAndValue");
            Assert.EndsWith("..ctor(string,string)", placeholder.Before!.SymbolId);
            Assert.EndsWith("..ctor(string,string,bool)", placeholder.After!.SymbolId);
            Assert.Contains("[bool isKey = false]", placeholder.After.Signature);
            var guard = Assert.Single(placeholder.Children, node => node.Label == "if (isKey)");
            Assert.Equal('+', guard.Mark);
            Assert.Equal("string.Concat", Assert.Single(guard.Children).Label);
        }
        else Assert.Contains(loop.Children, node => node.Label == "? new" && node.After!.SymbolId is null);
    }

    private static void VerifyOmittedIndexerWrites(DiffResult result)
    {
        var map = Assert.Single(result.Trees, node => node.Label == "MultiplexingMiddleware.MapAsync");
        Assert.Equal("signature changed", map.Detail);
        Assert.Equal(["IResponseAggregatorFactory.Get → InMemoryResponseAggregatorFactory.Get", "IResponseAggregator.Aggregate"],
            map.Children.Select(node => node.Label));
        var aggregate = map.Children[1];
        Assert.Equal("possible", aggregate.After!.Dispatch);
        Assert.Equal(2, aggregate.After.TargetIds.Count);
        Assert.Contains(aggregate.After.TargetIds, identity => identity.Contains("SimpleJsonResponseAggregator.Aggregate", StringComparison.Ordinal));
        Assert.Contains(aggregate.After.TargetIds, identity => identity.Contains("UserDefinedResponseAggregator.Aggregate", StringComparison.Ordinal));
        Assert.DoesNotContain(Descendants(map.Children), node => node.Label.Contains("CurrentAggregateRouteKey", StringComparison.Ordinal));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
