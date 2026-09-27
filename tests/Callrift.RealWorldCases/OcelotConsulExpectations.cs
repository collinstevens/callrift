using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotConsulExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored)
            Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(1034, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        }
        if (!focused)
        {
            var root = Assert.Single(result.Trees);
            Assert.Equal(restored ? "LoadBalancingMiddleware.Invoke" : "Consul.BuildServices", root.Label);
            Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
            Assert.Equal(root.Before.Signature, root.After.Signature);
            var affected = Assert.Single(root.Children, node => node.Mark == '~');
            Assert.Equal("changes below depth limit", affected.Detail);
            return;
        }
        Assert.Equal(4, result.Trees.Count);
        var host = Assert.Single(result.Trees, node => node.Label == "DefaultConsulServiceBuilder.GetDownstreamHost");
        Assert.Equal(host.Before!.SymbolId, host.After!.SymbolId);
        Assert.Equal(host.Before.Signature, host.After.Signature);
        Assert.StartsWith("protected virtual ", host.After.Signature);
        var check = Assert.Single(host.Children, node => node.Label == (restored ? "string.IsNullOrEmpty" : "? string.IsNullOrEmpty"));
        Assert.Equal('+', check.Mark);
        Assert.Null(check.Before);
        Assert.Equal(restored ? "string.IsNullOrEmpty" : "? string.IsNullOrEmpty", check.Label);
        Assert.Equal(97, Assert.Single(check.After!.CallSites).Line);
        if (restored)
            Assert.Equal("metadata:System.Runtime::string.IsNullOrEmpty(string)", check.After.SymbolId);
        else
            Assert.Null(check.After.SymbolId);
        var hostAndPort = Assert.Single(result.Trees, node => node.Label == "DefaultConsulServiceBuilder.GetServiceHostAndPort");
        Assert.Equal("DefaultConsulServiceBuilder.GetDownstreamHost", hostAndPort.Children[0].Label);
        var constructor = hostAndPort.Children[^1];
        Assert.Equal(restored ? "new ServiceHostAndPort" : "? new", constructor.Label);
        if (restored)
            Assert.Equal("src/Ocelot/Values/ServiceHostAndPort.cs", constructor.After!.Definition!.Path);
        var services = Assert.Single(result.Trees, node => node.Label == "DefaultConsulServiceBuilder.BuildServices");
        Assert.Contains(Descendants(services.Children), node => node.Label == "DefaultConsulServiceBuilder.GetDownstreamHost"
            && node.Children.Any(child => child.Mark == '+'));
        if (restored)
        {
            Assert.Equal(["if (node != null)", "else (!(node != null))"], host.Children.Where(node => node.Mark == '-').Select(node => node.Label));
            var preferred = Assert.Single(host.Children, node => node.Label == "if (!string.IsNullOrEmpty(entry?.Service?.Address))");
            Assert.Equal(["ServiceEntry.get_Service", "AgentService.get_Address"], preferred.Children.Select(node => node.Label));
            var fallback = Assert.Single(host.Children, node => node.Label == "else (!(!string.IsNullOrEmpty(entry?.Service?.Address)))");
            Assert.Equal(["if (node is not null)", "if (node?.Address is null)"], fallback.Children.Select(node => node.Label));
            Assert.Equal("Node.get_Address", Assert.Single(fallback.Children[0].Children).Label);
            Assert.Equal("Node.get_Name", Assert.Single(Assert.Single(fallback.Children[1].Children).Children).Label);
            Assert.All(Descendants(new[] { preferred, fallback }), node => Assert.Equal('+', node.Mark));
            Assert.Equal(["DefaultConsulServiceBuilder.GetDownstreamHost", "ServiceEntry.get_Service", "AgentService.get_Port", "new ServiceHostAndPort"], hostAndPort.Children.Select(node => node.Label));
        }
        else
            Assert.All(Descendants(result.Trees).Where(node => node.Mark != ' '), node =>
            {
                Assert.Equal('+', node.Mark);
                Assert.Equal(check.Label, node.Label);
            });
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
