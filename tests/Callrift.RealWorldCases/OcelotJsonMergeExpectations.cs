using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotJsonMergeExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored) Assert.Empty(result.Diagnostics);
        else Assert.Equal(1150, result.Diagnostics.Count);
        Assert.Equal(focused ? 3 : restored ? 30 : 40, result.Trees.Count);
        if (!focused)
        {
            var cache = Assert.Single(result.Trees, node => node.Label == "ConfigurationBuilderExtensions.get_MergedConfigJson");
            Assert.Equal('+', cache.Mark);
            Assert.Equal("possible initialization of ConfigurationBuilderExtensions", Assert.Single(cache.Children).Label);
            if (!restored)
                Assert.Equal(6, result.Trees.Count(node => node.Label.StartsWith("DefaultInfo.get_", StringComparison.Ordinal) || node.Label.StartsWith("DefaultInfo.set_", StringComparison.Ordinal)));
            return;
        }
        var merge = Assert.Single(result.Trees, node => node.Label == "ConfigurationBuilderExtensions.GetMergedOcelotJson");
        var schema = Assert.Single(merge.Children, node => node.Label == "ConfigurationBuilderExtensions.GuardSchema");
        Assert.Equal('+', schema.Mark);
        var properties = new[] { "Aggregates", "Routes", "DynamicRoutes", "GlobalConfiguration" };
        Assert.Equal(properties.SelectMany(property => new[] { "FileConfiguration.get_" + property, "if (configuration." + property + " is null)" }), schema.Children.Select(node => node.Label));
        foreach (var property in properties)
        {
            var guard = Assert.Single(schema.Children, node => node.Label == "if (configuration." + property + " is null)");
            Assert.Equal("FileConfiguration.set_" + property, guard.Children[^1].Label);
        }
        Assert.Equal(new[]
        {
            "IConfigurationBuilder.get_Properties",
            "possible initialization of ConfigurationBuilderExtensions",
            "ConfigurationBuilderExtensions.set_MergedConfigJObject",
            "IDictionary<TKey, TValue>.set_Item",
            restored ? "jobj.ToString" : "? jobj.ToString",
            "IConfigurationBuilder.get_Properties",
            "ConfigurationBuilderExtensions.set_MergedConfigJson",
            "IDictionary<TKey, TValue>.set_Item",
        }, merge.Children.TakeLast(8).Select(node => node.Label));
        var cached = Assert.Single(merge.Children, node => node.Label == "ConfigurationBuilderExtensions.set_MergedConfigJObject");
        Assert.StartsWith("private static ", cached.After!.Signature);
        Assert.Equal(166, Assert.Single(cached.After.CallSites).Line);
        var sections = Assert.Single(result.Trees, node => node.Label == "ConfigurationBuilderExtensions.OcelotMergeConfiguration");
        Assert.Equal("if (isGlobal)", sections.Children[0].Label);
        Assert.Equal("ConfigurationBuilderExtensions.OcelotJMergeSection", sections.Children[1].Label);
    }
}
