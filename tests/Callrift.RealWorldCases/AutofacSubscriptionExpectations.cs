using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacSubscriptionExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        Assert.Equal(15, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.All(result.Diagnostics.Where(diagnostic => diagnostic.Code == "unresolved-call"), diagnostic =>
            Assert.Equal("bench/Autofac.BenchmarkProfiling/Program.cs", diagnostic.Location!.Path));
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "test-project-inferred");
        if (!focused)
        {
            Assert.Equal(80, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "ComponentRegistrationExtensions.ConfigurePipeline" && node.Mark == '+');
            Assert.Contains(result.Trees, node => node.Label == "ComponentRegistrationLifetimeDecorator.ConfigurePipeline" && node.Mark == '-');
            Assert.Contains(result.Trees, node => node.Label == "ComponentRegistrationLifetimeDecorator.remove_PipelineBuilding");
            Assert.Contains(result.Trees, node => node.Label == "Container.BeginLifetimeScope");
            Assert.Contains(result.Trees, node => node.Label == "RegistrationExtensions.RegisterDecorator");
            Assert.DoesNotContain(result.Trees, node => node.Label == "new FactoryGenerator");
            var indexer = Assert.Single(result.Trees, node => node.Label == "KeyedServiceIndex<TKey, TValue>.get_Item");
            Assert.Equal(["KeyedServiceIndex<TKey, TValue>.GetService", "ResolutionExtensions.ResolveService"], indexer.Children.Select(node => node.Label));
            foreach (var label in new[] { "RegistrationExtensions.AsImplementedInterfaces", "RegistrationExtensions.AsSelf" })
            {
                var registration = Assert.Single(result.Trees, node => node.Label == label);
                var activator = Assert.Single(registration.Children, node => node.Label == "IConcreteActivatorData.get_Activator");
                Assert.Equal("possible", activator.After!.Dispatch);
                Assert.Contains(activator.Children, node => node.Label == "⇢ GeneratedFactoryActivatorData.get_Activator" && node.HasChanges);
            }
            return;
        }
        Assert.Equal(7, result.Trees.Count);
        var extension = Assert.Single(result.Trees, node => node.Label == "ComponentRegistrationExtensions.ConfigurePipeline");
        Assert.Equal('+', extension.Mark);
        Assert.Contains("this Autofac.Core.IComponentRegistration componentRegistration", extension.After!.Signature);
        Assert.Equal(["if (componentRegistration is null)", "if (configurationAction is null)", "callback", "IComponentRegistration.add_PipelineBuilding"],
            extension.Children.Select(node => node.Label));
        Assert.All(extension.Children.Take(2), guard => Assert.Equal("new ArgumentNullException", Assert.Single(guard.Children).Label));
        Assert.Equal("configurationAction", Assert.Single(extension.Children[2].Children).Label);
        var subscription = extension.Children[3];
        Assert.Equal("possible", subscription.After!.Dispatch);
        Assert.Equal(2, subscription.After.TargetIds.Count);
        Assert.Equal(63, Assert.Single(subscription.After.CallSites).Line);
        var target = Assert.Single(subscription.Children, node => node.Label == "⇢ ComponentRegistration.add_PipelineBuilding");
        VerifyLateSubscriptionGuard(target);
        var removed = Assert.Single(result.Trees, node => node.Label == "ComponentRegistration.ConfigurePipeline");
        Assert.Equal('-', removed.Mark);
        Assert.Null(removed.After);
        Assert.Equal(["if (configurationAction is null)", "if (_builtComponentPipeline is object)", "callback", "ComponentRegistration.add_PipelineBuilding"],
            removed.Children.Select(node => node.Label));
        var add = Assert.Single(result.Trees, node => node.Label == "ComponentRegistration.add_PipelineBuilding");
        Assert.Equal(add.Before!.SymbolId, add.After!.SymbolId);
        Assert.Equal(203, add.Before.Definition!.Line);
        Assert.Equal(207, add.After.Definition!.Line);
        VerifyLateSubscriptionGuard(add);
        var remove = Assert.Single(result.Trees, node => node.Label == "ComponentRegistration.remove_PipelineBuilding");
        Assert.Equal('~', remove.Mark);
        Assert.Equal("body changed; visible calls unchanged", remove.Detail);
        Assert.Empty(remove.Children);
        var decorator = Assert.Single(result.Trees, node => node.Label == "ComponentRegistrationLifetimeDecorator.add_PipelineBuilding");
        Assert.Equal(decorator.Before!.SymbolId, decorator.After!.SymbolId);
        var forwarding = Assert.Single(decorator.Children);
        Assert.Single(forwarding.Before!.TargetIds);
        Assert.Equal(2, forwarding.After!.TargetIds.Count);
        Assert.Equal("possible", forwarding.After.Dispatch);
        Assert.Contains(forwarding.Children, node => node.Label == "⇢ ComponentRegistrationLifetimeDecorator.add_PipelineBuilding" && node.Omission?.Reason == "cycle");
        var pipeline = Assert.Single(result.Trees, node => node.Label == "ComponentRegistration.BuildResolvePipeline");
        Assert.Equal(pipeline.Before!.SymbolId, pipeline.After!.SymbolId);
        Assert.Equal(["if (PipelineBuilding is object)", "if (_pipelineBuildEvent is object)", "ComponentRegistration.BuildResolvePipeline", "ComponentRegistration.set_ResolvePipeline"],
            pipeline.Children.Select(node => node.Label));
        var setter = pipeline.Children[^1];
        Assert.Equal(' ', setter.Mark);
        Assert.Equal(setter.Before!.SymbolId, setter.After!.SymbolId);
        Assert.Contains("protected Autofac.Core.Registration.ComponentRegistration.set_ResolvePipeline", setter.After.Signature);
        Assert.Equal(248, Assert.Single(setter.After.CallSites).Line);
        var oldInvocation = Assert.Single(pipeline.Children[0].Children);
        var newInvocation = Assert.Single(pipeline.Children[1].Children);
        Assert.Equal("PipelineBuilding.Invoke", oldInvocation.Label);
        Assert.Equal("_pipelineBuildEvent.Invoke", newInvocation.Label);
        Assert.Equal(oldInvocation.Before!.SymbolId, newInvocation.After!.SymbolId);
        Assert.DoesNotContain(pipeline.Children, node => node.Label == "if (_builtComponentPipeline is object)");
    }

    private static void VerifyLateSubscriptionGuard(DiffNode node)
    {
        var guard = Assert.Single(node.Children);
        Assert.Equal("if (_builtComponentPipeline is object)", guard.Label);
        Assert.Equal('+', guard.Mark);
        Assert.Equal(["ComponentRegistrationResources.get_PipelineAlreadyBuilt", "new InvalidOperationException"], guard.Children.Select(node => node.Label));
        var exception = guard.Children[1];
        Assert.Equal("new InvalidOperationException", exception.Label);
        Assert.Equal(211, Assert.Single(exception.After!.CallSites).Line);
    }
}
