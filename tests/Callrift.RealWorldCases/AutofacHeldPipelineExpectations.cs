using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacHeldPipelineExpectations
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
            Assert.Equal(workspace ? 101 : 134, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "KeyedServiceIndex<TKey, TValue>.get_Item");
            Assert.Contains(result.Trees, node => node.Label == "RegistrationExtensions.AsImplementedInterfaces");
            Assert.DoesNotContain(result.Trees, node => node.Label == "new FactoryGenerator");
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("DefaultRegisteredServicesTracker.HoldForAdditionalService", root.Label);
        Assert.Equal(["ServiceRegistrationInfo.get_IsInitializing", "if (!additionalInfo.IsInitializing)",
            "ServiceRegistrationInfo.IsSourceQueued", "if (additionalInfo.IsSourceQueued(source))"], root.Children.Select(node => node.Label));
        var getter = root.Children[0];
        Assert.Equal(' ', getter.Mark);
        Assert.Equal("ServiceRegistrationInfo.get_IsInitialized", Assert.Single(getter.Children).Label);
        Assert.Equal(682, Assert.Single(getter.After!.CallSites).Line);
        var initialization = Assert.Single(root.Children[1].Children);
        Assert.Equal("DefaultRegisteredServicesTracker.BeginServiceInfoInitialization", initialization.Label);
        Assert.Equal(684, Assert.Single(initialization.After!.CallSites).Line);
        var queued = root.Children[3];
        var removed = Assert.Single(queued.Children, node => node.Mark == '-');
        Assert.Equal("if (_ephemeralServiceInfo is null)", removed.Label);
        var pipeline = Assert.Single(removed.Children);
        Assert.Equal("IComponentRegistration.BuildResolvePipeline", pipeline.Label);
        Assert.Null(pipeline.After);
        Assert.Equal("possible", pipeline.Before!.Dispatch);
        Assert.Equal(694, Assert.Single(pipeline.Before.CallSites).Line);
        var deferred = Assert.Single(queued.Children, node => node.Mark == ' ');
        Assert.Equal("DefaultRegisteredServicesTracker.DeferSourceImplementation", deferred.Label);
        Assert.Equal(697, Assert.Single(deferred.Before!.CallSites).Line);
        Assert.Equal(692, Assert.Single(deferred.After!.CallSites).Line);
    }
}
