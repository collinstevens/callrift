using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacModuleExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("callbacks-are-possible-calls", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        if (workspace) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(179, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        }
        if (!focused)
        {
            Assert.Equal(workspace ? 106 : 128, result.Trees.Count);
            Assert.Equal(result.Trees.Count, result.Trees.Select(node => node.After!.SymbolId).Distinct().Count());
            Assert.DoesNotContain(result.Trees, node => node.Label == "new ReflectionCacheSet");
            Assert.Contains(result.Trees, node => node.Label == "KeyedServiceIndex<TKey, TValue>.get_Item");
            Assert.Contains(result.Trees, node => node.Label == "RegistrationExtensions.AsImplementedInterfaces");
            Assert.Equal(4, result.Trees.Count(node => node.Label == "ModuleRegistrationExtensions.RegisterAssemblyModules"));
            Assert.Equal(2, result.Trees.Count(node => node.Label == "ModuleRegistrationExtensions.RegisterModule"));
            return;
        }
        Assert.Equal(["new InternalReflectionCaches", "Module.AttachToRegistrations", "Module.AttachToSources"],
            result.Trees.Select(node => node.Label));
        var caches = result.Trees[0];
        var added = caches.Children.Where(node => node.Mark == '+').ToArray();
        Assert.Equal(2, added.Length);
        Assert.Equal([28, 33], added.Select(node => Assert.Single(node.After!.CallSites).Line));
        Assert.All(added, node =>
        {
            Assert.Equal("ReflectionCacheSet.GetOrCreateCache", node.Label);
            Assert.Null(node.Before);
            var creation = Assert.Single(node.Children, child => child.Label == "new ReflectionCacheDictionary<TKey, TValue>");
            Assert.Equal("callback", creation.After!.Relation);
            Assert.Equal("new ConcurrentDictionary<TKey, TValue>", Assert.Single(creation.Children).Label);
            var setter = Assert.Single(node.Children, child => child.Label == "ReflectionCacheDictionary<TKey, TValue>.set_Usage");
            Assert.Equal("callback", setter.After!.Relation);
            Assert.True(node.Children.ToList().IndexOf(creation) < node.Children.ToList().IndexOf(setter));
            var failure = Assert.Single(Descendants(node.Children), child => child.Label.EndsWith("new InvalidOperationException", StringComparison.Ordinal));
            Assert.Equal(workspace ? "resolved" : "unresolved", failure.After!.Binding);
        });
        VerifyHook(result.Trees[1], "Module.AttachToComponentRegistration", "Registered", 155);
        VerifyHook(result.Trees[2], "Module.AttachToRegistrationSource", "RegistrationSourceAdded", 186);
    }

    private static void VerifyHook(DiffNode root, string hook, string eventName, int line)
    {
        var guard = Assert.Single(root.Children, node => node.Label == "if (componentRegistry == null)");
        Assert.Equal(' ', guard.Mark);
        Assert.Equal("new ArgumentNullException", Assert.Single(guard.Children).Label);
        var removed = Assert.Single(root.Children, node => node.Label == "callback" && node.Mark == '-');
        Assert.Equal(3, removed.Children.Count);
        var original = removed.Children[^1];
        Assert.Equal(hook, original.Label);
        var lookup = Assert.Single(root.Children, node => node.Label == "cache.GetOrAdd");
        Assert.Equal('+', lookup.Mark);
        Assert.Equal(["t.GetMethod", "if (method is not null)"], lookup.Children.Select(node => node.Label));
        Assert.Equal("callback", lookup.Children[0].After!.Relation);
        Assert.Equal(["MemberInfo.get_DeclaringType", "Type.op_Inequality"], lookup.Children[1].Children.Select(node => node.Label));
        var condition = Assert.Single(root.Children, node => node.Label == "if (overrides)");
        Assert.Equal('+', condition.Mark);
        var callback = Assert.Single(condition.Children, node => node.Label == "callback");
        Assert.Equal("callback", callback.After!.Relation);
        Assert.Equal(removed.Children.Select(node => node.Before!.SymbolId), callback.Children.Select(node => node.After!.SymbolId));
        var moved = callback.Children[^1];
        Assert.Equal(hook, moved.Label);
        Assert.Equal(original.Before!.SymbolId, moved.After!.SymbolId);
        Assert.Equal(line, Assert.Single(moved.After.CallSites).Line);
        var previousSubscription = Assert.Single(root.Children, node => node.Mark == '-'
            && node.Before?.SymbolId?.Contains(".add_" + eventName + "(", StringComparison.Ordinal) == true);
        var subscription = Assert.Single(condition.Children, node => node != callback);
        Assert.Equal(previousSubscription.Before!.SymbolId, subscription.After!.SymbolId);
        Assert.Equal("possible", subscription.After.Dispatch);
        Assert.Contains("::Autofac.Core.Registration.ComponentRegistryBuilder.add_" + eventName + "(", Assert.Single(subscription.After.TargetIds));
        Assert.Equal(line - 1, Assert.Single(subscription.After.CallSites).Line);
        Assert.Contains(subscription.Children, node => node.Label.StartsWith("foreach (", StringComparison.Ordinal));
        Assert.Contains(Descendants(subscription.Children), node => node.Label == "value");
        var labels = root.Children.Select(node => node.Label).ToList();
        var shared = labels.IndexOf("ReflectionCacheSet.get_Shared");
        Assert.True(shared >= 0);
        Assert.Equal("ReflectionCacheSet.get_Internal", labels[shared + 1]);
        Assert.Equal("InternalReflectionCaches.get_ModuleOverrides" + hook["Module.".Length..], labels[shared + 2]);
        Assert.Equal("cache.GetOrAdd", labels[shared + 3]);
        Assert.Equal("if (overrides)", labels[shared + 4]);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
