using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotWebSocketExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored) Assert.Empty(result.Diagnostics);
        else Assert.Equal(1050, result.Diagnostics.Count);
        Assert.Equal(focused ? 4 : restored ? 7 : 6, result.Trees.Count);
        var invoke = Assert.Single(result.Trees, node => node.Label == "SecurityMiddleware.Invoke");
        Assert.Equal('~', invoke.Mark);
        Assert.Equal("signature changed", invoke.Detail);
        var loop = Assert.Single(invoke.Children, node => node.Label == "foreach (var policy in _policies)");
        var failure = Assert.Single(loop.Children, node => node.Label == "if (result.IsError)");
        Assert.Contains(failure.Children, node => node.Label == "SecurityMiddleware.HandleWebSocketErrors" && node.Mark == '+');
        if (!focused) return;
        var pipeline = Assert.Single(result.Trees, node => node.Label == "OcelotPipelineExtensions.ConfigureWebSockets");
        Assert.Contains(pipeline.Children, node => node.Label.Contains("UseMiddleware<SecurityMiddleware>", StringComparison.Ordinal) && node.Mark == '+');
        var helper = Assert.Single(result.Trees, node => node.Label == "SecurityMiddleware.HandleWebSocketErrors");
        Assert.Equal('+', helper.Mark);
        Assert.Null(helper.Before);
        if (!restored)
        {
            Assert.Empty(helper.Children);
            return;
        }
        Assert.Equal(["HttpContext.get_WebSockets", "WebSocketManager.get_IsWebSocketRequest", "if (context.WebSockets.IsWebSocketRequest)"], helper.Children.Select(node => node.Label));
        var guard = helper.Children[^1];
        Assert.Equal(["HttpContext.get_Response", "HttpResponse.set_StatusCode"], guard.Children.Select(node => node.Label));
        var setter = guard.Children[^1];
        Assert.Equal('+', setter.Mark);
        Assert.Equal(55, Assert.Single(setter.After!.CallSites).Line);
        Assert.EndsWith("::Microsoft.AspNetCore.Http.HttpResponse.set_StatusCode(int)", setter.After.SymbolId);
    }
}
