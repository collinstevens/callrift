using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacAnyKeyExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.True(result.Truncated);
        Assert.Equal("partial", result.Coverage.Status);
        if (workspace) Assert.Empty(result.Diagnostics);
        else Assert.Equal(183, result.Diagnostics.Count);
        if (!focused)
        {
            Assert.Equal(workspace ? 101 : 140, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "KeyedServiceIndex<TKey, TValue>.get_Item");
            Assert.Contains(result.Trees, node => node.Label == "RegistrationExtensions.AsImplementedInterfaces");
            Assert.DoesNotContain(result.Trees, node => node.Label == "new FactoryGenerator");
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("AnyKeyRegistrationSource.RegistrationsFor", root.Label);
        var removed = root.Children.Where(node => node.Mark == '-').ToArray();
        Assert.Equal(["_adapterCache.TryGetValue", "_adapterCache.TryAdd"], removed.Select(node => node.Label));
        Assert.Equal([56, 81], removed.Select(node => Assert.Single(node.Before!.CallSites).Line));
        Assert.All(removed, node => Assert.Null(node.After));
        var keyGuard = Assert.Single(root.Children, node => node.Label == "if (!(service is not KeyedService keyedService))");
        Assert.Equal(["KeyedService.get_ServiceKey", "KeyedService.IsAnyKey"], keyGuard.Children.Select(node => node.Label));
        var typeGuard = Assert.Single(root.Children, node => node.Label ==
            "if (!(service is not KeyedService keyedService || KeyedService.IsAnyKey(keyedService.ServiceKey)))");
        Assert.Equal(["KeyedService.get_ServiceType", "InternalTypeExtensions.IsCollectionServiceType"], typeGuard.Children.Select(node => node.Label));
        var loop = Assert.Single(root.Children, node => node.Label == "for (i < anyKeyRegistrations.Length)");
        Assert.Equal(["Array.get_Length", "AnyKeyRegistrationSource.CreateAdapterRegistration"], loop.Children.Select(node => node.Label));
        var adapter = loop.Children[1];
        var labels = adapter.Children.Select(node => node.Label).ToArray();
        Assert.Equal(["ServiceRegistration.get_Registration", "IComponentRegistration.get_Metadata",
            "new Dictionary<string, object?>", "Dictionary<TKey, TValue>.set_Item", "KeyedService.get_ServiceType",
            "new DelegateActivator", "Guid.NewGuid", "ServiceRegistration.get_Registration",
            "IComponentRegistration.get_Lifetime", "ServiceRegistration.get_Registration", "IComponentRegistration.get_Sharing",
            "ServiceRegistration.get_Registration", "IComponentRegistration.get_Ownership", "ServiceRegistration.get_Registration",
            "new ComponentRegistration"], labels);
        Assert.All(adapter.Children.Where(node => node.Label.StartsWith("IComponentRegistration.get_", StringComparison.Ordinal)),
            node => Assert.Equal("possible", node.After!.Dispatch));
        var callback = adapter.Children[5];
        Assert.Equal(["KeyedService.get_AnyKey", "KeyedService.get_ServiceType", "new KeyedService", "new ResolveRequest",
            "IComponentContext.ResolveComponent"], callback.Children.Select(node => node.Label));
        Assert.All(callback.Children, node => Assert.Equal("callback", node.After!.Relation));
    }
}
