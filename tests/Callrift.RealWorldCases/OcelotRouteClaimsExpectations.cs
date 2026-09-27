using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotRouteClaimsExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored) Assert.Empty(result.Diagnostics);
        else Assert.Equal(1135, result.Diagnostics.Count);
        Assert.Equal(focused ? 3 : restored ? 5 : 9, result.Trees.Count);
        var configure = Assert.Single(result.Trees, node => node.Label == "RouteClaimsRequirementPostConfigureOptions.PostConfigure");
        Assert.Equal('+', configure.Mark);
        Assert.Null(configure.Before);
        Assert.Equal("FileConfiguration.get_Routes", configure.Children[0].Label);
        var coalescing = Assert.Single(configure.Children, node => node.Label == "if (options.Routes is null)");
        Assert.Equal("FileConfiguration.set_Routes", Assert.Single(coalescing.Children).Label);
        var loop = Assert.Single(configure.Children, node => node.Label == "for (i < options.Routes.Count)");
        var nonempty = Assert.Single(loop.Children, node => node.Label == "if (requirements.Count > 0)");
        Assert.Equal("FileConfiguration.get_Routes", nonempty.Children[0].Label);
        Assert.Equal("FileRoute.set_RouteClaimsRequirement", nonempty.Children[^1].Label);
        if (!focused) return;
        Assert.Equal(["FileConfiguration.get_Routes", "List<T>.get_Item", "FileRoute.set_RouteClaimsRequirement"], nonempty.Children.Select(node => node.Label));
        var setter = nonempty.Children[^1].After!;
        Assert.Equal("src/Configuration/File/FileRoute.cs", setter.Definition!.Path);
        Assert.Equal(31, Assert.Single(setter.CallSites).Line);
        var authorization = Assert.Single(result.Trees, node => node.Label == "Features.AddOcelotAuthorization");
        Assert.Equal('+', authorization.Mark);
        Assert.Contains(authorization.Children, node => node.Label == "new RouteClaimsRequirementPostConfigureOptions");
        var builder = Assert.Single(result.Trees, node => node.Label == "new OcelotBuilder");
        Assert.Contains(builder.Children, node => node.Label == "OcelotBuilder.get_MvcCoreBuilder" && node.Mark == '+');
        Assert.Contains(builder.Children, node => node.Label == "Features.AddOcelotAuthorization" && node.Mark == '+');
    }
}
