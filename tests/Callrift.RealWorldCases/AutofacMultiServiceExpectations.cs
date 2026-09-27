using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacMultiServiceExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.True(result.Truncated);
        Assert.Equal("partial", result.Coverage.Status);
        if (workspace) Assert.Empty(result.Diagnostics);
        else Assert.Equal(187, result.Diagnostics.Count);
        if (!focused)
        {
            Assert.Equal(workspace ? 101 : 142, result.Trees.Count);
            Assert.DoesNotContain(result.Trees, node => node.Label == "new FactoryGenerator");
            Assert.Contains(result.Trees, node => node.Label == "KeyedServiceIndex<TKey, TValue>.get_Item");
            Assert.Equal(!workspace, result.Trees.Any(node => node.Label == "OpenGenericMultiServiceBenchmark.BuildAndResolveAllServices"));
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("DefaultRegisteredServicesTracker.PopulateServiceInfo", root.Label);
        Assert.Equal([
            "ServiceRegistrationInfo.get_IsInitializing", "if (!info.IsInitializing)",
            "ServiceRegistrationInfo.get_InitializationDepth", "ServiceRegistrationInfo.set_InitializationDepth",
            "while (info.HasSourcesToQuery)"
        ], root.Children.Select(node => node.Label));
        Assert.Equal("ServiceRegistrationInfo.get_IsInitialized", Assert.Single(root.Children[0].Children).Label);
        var begin = Assert.Single(root.Children[1].Children).Children[^1];
        Assert.Equal("ServiceRegistrationInfo.BeginInitialization", begin.Label);
        Assert.Equal("ServiceRegistrationInfo.set_IsInitialized", begin.Children[0].Label);
        var loop = root.Children[^1];
        Assert.Equal("ServiceRegistrationInfo.get_HasSourcesToQuery", loop.Children[0].Label);
        Assert.Equal("ServiceRegistrationInfo.DequeueNextSource", loop.Children[1].Label);
        var initializationGuard = Assert.Single(loop.Children[1].Children).Children[^1];
        Assert.Equal("if (!IsInitializing)", initializationGuard.Label);
        Assert.Equal(workspace ? "ServiceRegistrationInfoResources.get_NotDuringInitialization" : "? new InvalidOperationException",
            Assert.Single(initializationGuard.Children).Label);
        var held = loop.Children[2];
        Assert.Equal("if (held is not null)", held.Label);
        Assert.Equal('+', held.Mark);
        var apply = Assert.Single(held.Children);
        Assert.Equal("DefaultRegisteredServicesTracker.TryApplyDeferredSourceImplementations", apply.Label);
        Assert.Equal("ServiceRegistrationInfo.AddImplementation", Assert.Single(Assert.Single(apply.Children).Children).Label);
        var sources = loop.Children[3];
        Assert.Equal("IRegistrationSource.RegistrationsFor", sources.Label);
        Assert.Equal("possible", sources.After!.Dispatch);
        Assert.Equal(13, sources.Children.Count);
        var provided = loop.Children[^1];
        var additional = provided.Children[0];
        var services = additional.Children[0];
        Assert.Equal("IComponentRegistration.get_Services", services.Label);
        Assert.Equal("possible", services.After!.Dispatch);
        Assert.Equal(["⇢ ComponentRegistration.get_Services", "⇢ ComponentRegistrationLifetimeDecorator.get_Services"],
            services.Children.Select(node => node.Label));
        var additionalLoop = additional.Children[1];
        Assert.Equal("ServiceRegistrationInfo.get_IsInitialized", additionalLoop.Children[1].Label);
        Assert.Equal("DefaultRegisteredServicesTracker.InitializeOrSkipSource", additionalLoop.Children[^2].Label);
        Assert.Equal('-', additionalLoop.Children[^2].Mark);
        Assert.Equal("DefaultRegisteredServicesTracker.HoldOrApplyForAdditionalService", additionalLoop.Children[^1].Label);
        Assert.Equal('+', additionalLoop.Children[^1].Mark);
        Assert.Contains("[bool originatedFromDynamicSource = false]", provided.Children[1].Before!.Signature);
        Assert.Contains("[Autofac.Core.IRegistrationSource? originatingSource = null]", provided.Children[2].After!.Signature);
        Assert.All(provided.Children.Skip(1), node => Assert.Equal([
            "⇢ DefaultRegisteredServicesTracker.AddRegistration", "⇢ ScopeRestrictedRegisteredServicesTracker.AddRegistration"
        ], node.Children.Select(child => child.Label)));
    }
}
