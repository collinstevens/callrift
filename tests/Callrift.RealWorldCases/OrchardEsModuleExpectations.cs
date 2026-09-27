using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OrchardEsModuleExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.True(result.Truncated);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        var nodes = Descendants(result.Trees).ToArray();
        Assert.DoesNotContain(nodes.SelectMany(node => new[] { node.Before?.Definition, node.After?.Definition }),
            location => location?.Path.StartsWith("test/OrchardCore.Tests.Functional/", StringComparison.Ordinal) == true);
        if (focused)
            VerifyFocused(result, nodes);
        else
            VerifyRoots(result, nodes);
    }

    private static void VerifyFocused(DiffResult result, DiffNode[] nodes)
    {
        Assert.Equal(6, result.Trees.Count);
        string[] modules = ["AdminMenu", "ContentFields", "Cors", "Forms", "Localization", "Media", "Menu", "OpenId", "Seo", "Shortcodes", "Taxonomies"];
        var expected = modules.Select(module => $"OrchardCore.{module}.Services.{module}JSLocalizer.GetLocalizations(string)").ToArray();
        var calls = nodes.Where(node => node.Label == "IJSLocalizer.GetLocalizations").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call =>
        {
            Assert.Equal("possible", call.Before!.Dispatch);
            Assert.Equal("possible", call.After!.Dispatch);
            Assert.Equal([expected[5]], call.Before.TargetIds.Select(Symbol));
            Assert.Equal(expected, call.After.TargetIds.Select(Symbol));
            Assert.Equal(expected, call.Children.Select(child => Symbol(child.After!.SymbolId!)));
            Assert.All(call.Children, child =>
            {
                Assert.Equal("dispatchTarget", child.Kind);
                Assert.Equal("depth-limit", child.Omission!.Reason);
                Assert.Equal(Symbol(child.After!.SymbolId!) == expected[5] ? ' ' : '+', child.Mark);
            });
        });
        foreach (var type in new[] { "LocalizationSettings", "LocalizationService", "DefaultLocalizationService" })
        {
            var initializer = Assert.Single(result.Trees, node => node.Label == "initialization of " + type);
            var addition = Assert.Single(Descendants(initializer.Children), node => node.Mark == '+');
            Assert.Equal("string.IsNullOrEmpty", addition.Label);
        }
        var manifest = Assert.Single(nodes, node => node.Label == "ResourceManagementOptionsConfiguration.BuildManifest");
        Assert.Equal("signature changed", manifest.Detail);
        Assert.Equal(manifest.Before!.SymbolId, manifest.After!.SymbolId);
        Assert.Equal(manifest.Before.Signature!.Replace("private ", "private static ", StringComparison.Ordinal), manifest.After.Signature);
    }

    private static void VerifyRoots(DiffResult result, DiffNode[] nodes)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal(restored ? 424 : 389, result.Trees.Count);
        if (!restored)
        {
            Assert.Equal(16756, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(19, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
            return;
        }
        Assert.Equal(19, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        var cache = Assert.Single(result.Trees, node => node.Label == "XmlCommentCache.GenerateCacheEntries");
        var removed = Assert.Single(Descendants(cache.Children), node => node.Mark == '-');
        Assert.Equal("new XmlComment", removed.Label);
        Assert.Equal(1205, Assert.Single(removed.Before!.CallSites).Line);
        Assert.Contains(nodes, node => node.After?.Definition?.Path.EndsWith("/ShapeFactoryGenerator.g.cs", StringComparison.Ordinal) == true
            && node.Label.Contains("[interceptor in ", StringComparison.Ordinal));
    }

    private static string Symbol(string key) => key[(key.IndexOf("::", StringComparison.Ordinal) + 2)..];

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
