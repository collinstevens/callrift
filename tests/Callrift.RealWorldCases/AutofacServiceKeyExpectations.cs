using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacServiceKeyExpectations
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
            Assert.Equal(workspace ? 101 : 180, result.Trees.Count);
            Assert.DoesNotContain(result.Trees, node => node.Label == "new FactoryGenerator");
            Assert.Contains(result.Trees, node => node.Label == "KeyedServiceIndex<TKey, TValue>.get_Item");
            if (!workspace)
            {
                var properties = result.Trees.Where(node => node.Label.StartsWith("ContainerBuildInheritedMembersBenchmark.WideBase.", StringComparison.Ordinal)).ToArray();
                Assert.Equal(40, properties.Length);
                Assert.All(properties, node => Assert.Equal('+', node.Mark));
                Assert.Equal(20, properties.Count(node => node.Label.Contains(".get_", StringComparison.Ordinal)));
                Assert.Equal(20, properties.Count(node => node.Label.Contains(".set_", StringComparison.Ordinal)));
            }
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("ReflectionActivator.UsesServiceKeyAttribute", root.Label);
        Assert.Equal(2, root.Children.Count(node => node.Mark == '-'));
        var constructors = Assert.Single(root.Children, node => node.Label ==
            "foreach (var constructor in implementationType.GetConstructors(DeclaredMembers))");
        var parameterCheck = Assert.Single(Assert.Single(constructors.Children).Children);
        Assert.Equal("ServiceKeyAttributeCache.ParameterHasServiceKey", parameterCheck.Label);
        Assert.Equal(["ReflectionCacheSet.get_Shared", "ReflectionCacheSet.get_Internal",
            "InternalReflectionCaches.get_ServiceKeyParameterAttributes"], parameterCheck.Children.Take(3).Select(node => node.Label));
        var propertiesLoop = Assert.Single(root.Children, node => node.Label ==
            "foreach (var property in implementationType.GetProperties(DeclaredMembers))");
        var writable = Assert.Single(propertiesLoop.Children);
        Assert.Equal("if (property.CanWrite)", writable.Label);
        var propertyCheck = Assert.Single(writable.Children);
        Assert.Equal("ServiceKeyAttributeCache.PropertyHasServiceKey", propertyCheck.Label);
        Assert.Equal(["ReflectionCacheSet.get_Shared", "ReflectionCacheSet.get_Internal",
            "InternalReflectionCaches.get_ServiceKeyPropertyAttributes"], propertyCheck.Children.Select(node => node.Label));
        var baseType = Assert.Single(root.Children, node => node.Label == "if (baseType is not null && baseType != typeof(object))");
        var cached = Assert.Single(baseType.Children);
        Assert.Equal("ReflectionActivator.UsesServiceKeyAttributeCached", cached.Label);
        Assert.Equal(["ReflectionCacheSet.get_Shared", "ReflectionCacheSet.get_Internal",
            "InternalReflectionCaches.get_ServiceKeyUsageByType", "ReflectionCacheSet.Shared.Internal.ServiceKeyUsageByType.GetOrAdd"],
            cached.Children.Select(node => node.Label));
        var callback = Assert.Single(cached.Children[^1].Children);
        Assert.Equal("callback", callback.After!.Relation);
        Assert.Equal(root.After!.SymbolId, callback.After.SymbolId);
        Assert.Equal("cycle", callback.Omission?.Reason);
    }
}
