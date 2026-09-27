using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacFactoryCacheExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Contains("callbacks-are-possible-calls", result.Coverage.Limitations);
        if (result.Coverage.Mode == "msbuild")
            Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(182, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        }

        if (!focused)
        {
            Assert.Equal(result.Coverage.Mode == "msbuild" ? 101 : 125, result.Trees.Count);
            Assert.Contains(result.Trees, root => root.Label == "ContainerBuilder.RegisterBuildCallback");
            Assert.Contains(Descendants(result.Trees), node => node.Detail == "changes below depth limit");
            Assert.DoesNotContain(Descendants(result.Trees), node => node.Label.EndsWith("GeneratorCache.GetOrAdd", StringComparison.Ordinal));
            return;
        }

        Assert.Equal(2, result.Trees.Count);
        VerifyConstructor(result.Trees[0], "ServiceRegistration", 91, 93, 98, 4);
        VerifyConstructor(result.Trees[1], "ServiceOnly", 64, 66, 69, 3);
        Assert.Contains(result.Trees[1].Children, node => node.Label == "if (service == null)" && node.Mark == ' ');
        var initialization = Assert.Single(result.Trees[0].Children, node => node.Label == "possible initialization of FactoryGenerator");
        Assert.Equal('+', initialization.Mark);
        var initializer = Assert.Single(initialization.Children);
        Assert.Equal(2, initializer.Children.Count(node => node.Label == "new"));
        Assert.Contains(initializer.Children, node => node.Label == "ReflectionExtensions.GetConstructor");
    }

    private static void VerifyConstructor(DiffNode root, string cache, int lookupLine, int factoryLine, int closureLine, int parameters)
    {
        Assert.Equal("new FactoryGenerator", root.Label);
        Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
        Assert.Equal(root.Before.Signature, root.After.Signature);
        Assert.Contains(root.Children, node => node.Label == "FactoryGenerator.GetParameterMapping" && node.Mark == ' ');
        var removed = Assert.Single(root.Children, node => node.Label == "FactoryGenerator.CreateGenerator");
        Assert.Equal('-', removed.Mark);
        Assert.Null(removed.After);
        var lookup = Assert.Single(root.Children, node => node.Label == cache + "GeneratorCache.GetOrAdd");
        Assert.Equal('+', lookup.Mark);
        Assert.Equal("resolved", lookup.After!.Binding);
        Assert.Equal(lookupLine, Assert.Single(lookup.After.CallSites).Line);
        var factory = Assert.Single(lookup.Children);
        Assert.Equal("FactoryGenerator.Create" + cache + "Generator", factory.Label);
        Assert.Equal("callback", factory.After!.Relation);
        Assert.Equal(factoryLine, Assert.Single(factory.After.CallSites).Line);
        Assert.Equal(parameters, factory.Children.Count(node => node.Label == "Expression.Parameter"));
        Assert.Contains(factory.Children, node => node.Label == "activator.Compile");
        Assert.Contains(Descendants(factory.Children), node => node.Label == "if (arguments.Distinct().Count() != arguments.Length)");
        Assert.Contains(Descendants(factory.Children), node => node.Label == "Expression.Throw");
        var closure = Assert.Single(root.Children, node => node.Label == "callback");
        Assert.Equal("callback", closure.After!.Relation);
        var invocation = Assert.Single(closure.Children);
        Assert.Equal("compiledGenerator", invocation.Label);
        Assert.Equal(closureLine, Assert.Single(invocation.After!.CallSites).Line);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
