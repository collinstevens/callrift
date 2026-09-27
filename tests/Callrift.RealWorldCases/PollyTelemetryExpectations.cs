using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyTelemetryExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        if (!focused)
        {
            Assert.Equal(workspace ? 8 : 16, result.Trees.Count);
            Assert.DoesNotContain(result.Trees, node => node.Label is "new TelemetrySource" or "initialization of TelemetrySource");
            if (workspace)
            {
                var builders = result.Trees.Where(node => node.Label is "ResiliencePipelineBuilder.Build" or "ResiliencePipelineBuilder<TResult>.Build").ToArray();
                Assert.Equal(2, builders.Length);
                Assert.All(builders, node => Assert.Equal("ResiliencePipelineBuilderBase.get_ContextPool", node.Children[1].Label));
            }
            return;
        }

        var root = Assert.Single(result.Trees);
        Assert.Equal("new TelemetrySource", root.Label);
        Assert.Equal('+', root.Mark);
        var version = root.Children[1];
        Assert.Equal("TelemetrySource.GetVersion", version.Label);
        Assert.Equal("Type.get_Assembly", version.Children[0].Label);
        Assert.Equal(22, Assert.Single(version.Children[0].After!.CallSites).Line);
        if (workspace)
        {
            var attribute = Assert.Single(version.Children[2].Children);
            Assert.Equal("AssemblyInformationalVersionAttribute.get_InformationalVersion", attribute.Label);
            Assert.Equal(22, Assert.Single(attribute.After!.CallSites).Line);
        }
        var trimming = version.Children.TakeLast(4).ToArray();
        Assert.Equal(workspace ? "informationalVersion!.IndexOf" : "? informationalVersion!.IndexOf", trimming[0].Label);
        Assert.Equal(trimming[0].Label, trimming[2].Label);
        Assert.Equal(["if (index > 0)", "if (index > 0)"], new[] { trimming[1].Label, trimming[3].Label });
        Assert.Equal(workspace ? "informationalVersion.Substring" : "? informationalVersion.Substring", Assert.Single(trimming[1].Children).Label);
        Assert.EndsWith(".Meter..ctor(string,string)", root.Children[2].After!.SymbolId);
        Assert.Equal(14, Assert.Single(root.Children[2].After!.CallSites).Line);
    }
}
