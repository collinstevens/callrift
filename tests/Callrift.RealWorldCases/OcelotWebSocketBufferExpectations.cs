using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotWebSocketBufferExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(1118, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        }

        Assert.Equal(focused ? 3 : restored ? 6 : 11, result.Trees.Count);
        if (!focused)
        {
            if (restored)
            {
                Assert.Contains(result.Trees, node => node.Label == "WebSocketsProxyMiddleware.Invoke");
                var constructor = Assert.Single(result.Trees, node => node.Label == "new WebSocketsProxyMiddleware");
                Assert.Equal("signature changed", constructor.Detail);
                Assert.Contains("RequestDelegate next,", constructor.After!.Signature);
            }
            else
            {
                var sample = Assert.Single(result.Trees, node => node.Label == "samples/WebSocket/Program.cs::<top-level>");
                Assert.Equal('+', sample.Mark);
                Assert.Contains(sample.Children, node => node.Label == "OcelotPipelineConfiguration.set_WebSocketsMiddlewareType");
                Assert.Contains(sample.Children, node => node.Label == "OcelotPipelineConfiguration.set_WebSocketsMiddleware");
            }

            return;
        }

        var pump = Assert.Single(result.Trees, node => node.Label == "WebSocketsProxyMiddleware.PumpAsync");
        Assert.Equal("signature changed", pump.Detail);
        Assert.Contains("int bufferSize", pump.Before!.Signature);
        Assert.DoesNotContain("int bufferSize", pump.After!.Signature);
        Assert.NotEqual(pump.Before.SymbolId, pump.After.SymbolId);
        Assert.Equal(["WebSocketsProxyMiddleware.get_BufferSize", "ArgumentOutOfRangeException.ThrowIfNegativeOrZero",
            "WebSocketsProxyMiddleware.get_BufferSize", "while (true)"], pump.Children.Select(node => node.Label));
        var getters = pump.Children.Where(node => node.Label == "WebSocketsProxyMiddleware.get_BufferSize").ToArray();
        Assert.Equal([47, 48], getters.Select(node => Assert.Single(node.After!.CallSites).Line));
        Assert.All(getters, node =>
        {
            Assert.Equal('+', node.Mark);
            Assert.Equal(restored ? "direct" : "possible", node.After!.Dispatch);
            Assert.Equal(restored ? 1 : 2, node.After.TargetIds.Count);
            if (!restored) Assert.Contains(node.After.TargetIds, id => id.Contains("MyWebSocketsProxyMiddleware", StringComparison.Ordinal));
        });
        Assert.All(pump.Children[^1].Children, node => Assert.Equal(' ', node.Mark));

        var pipeline = Assert.Single(result.Trees, node => node.Label == "OcelotPipelineExtensions.BuildOcelotPipeline");
        Assert.EndsWith("app.UseWebSockets", pipeline.Children[2].Label);
        Assert.Equal('+', pipeline.Children[2].Mark);
        var map = pipeline.Children[3];
        Assert.EndsWith("app.MapWhen", map.Label);
        Assert.Equal(6, map.Children.Count(node => node.Mark == '-'));
        var callback = Assert.Single(map.Children, node => node.Mark == '+');
        Assert.Equal(restored ? "OcelotPipelineExtensions.ConfigureWebSockets" : "? app.ConfigureWebSockets", callback.Label);
        Assert.Equal(restored ? "resolved" : "unresolved", callback.After!.Binding);
        Assert.Contains(pipeline.Children, node => node.Label == "if (configuration.MapWhenOcelotPipeline != null)" && node.Mark == '-');
        Assert.Contains(pipeline.Children, node => node.Label == "foreach (var branch in configuration.MapWhenOcelotPipeline)" && node.Mark == '+');

        var configure = Assert.Single(result.Trees, node => node.Label == "OcelotPipelineExtensions.ConfigureWebSockets");
        Assert.Equal('+', configure.Mark);
        Assert.Equal(["OcelotPipelineConfiguration.get_WebSocketsMiddlewareType", "OcelotPipelineExtensions.UseIfNotNull",
            "OcelotPipelineConfiguration.get_WebSocketsMiddleware", "OcelotPipelineConfiguration.get_WebSocketsMiddlewareType",
            "OcelotPipelineExtensions.UseIfNotNull"], configure.Children.Skip(5).Select(node => node.Label));
        var typeRegistration = configure.Children[6];
        Assert.Equal(["Type.get_BaseType", "middlewareType.BaseType.Equals",
            "if (!middlewareType.BaseType.Equals(typeof(TMiddlewareBase)))", restored ? "builder.UseMiddleware" : "? builder.UseMiddleware"],
            typeRegistration.Children.Select(node => node.Label));
        Assert.Equal("new Exception", typeRegistration.Children[2].Children[^1].Label);
        Assert.DoesNotContain(typeRegistration.Children, node => node.Label.Contains("middlewareType is null", StringComparison.Ordinal));
        var delegateRegistration = configure.Children[^1];
        Assert.Contains("bool addDefault = true", delegateRegistration.After!.Signature);
        Assert.Equal(["if (middleware != null)", "else (!(middleware != null))"], delegateRegistration.Children.Select(node => node.Label));
        Assert.EndsWith("builder.Use", Assert.Single(delegateRegistration.Children[0].Children).Label);
        var fallback = Assert.Single(delegateRegistration.Children[1].Children);
        Assert.Equal("if (addDefault)", fallback.Label);
        Assert.EndsWith("builder.UseMiddleware<TMiddleware>", Assert.Single(fallback.Children).Label);
    }
}
