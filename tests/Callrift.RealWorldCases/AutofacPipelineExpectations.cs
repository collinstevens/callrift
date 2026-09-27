using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacPipelineExpectations
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
            Assert.Equal(workspace ? 103 : 134, result.Trees.Count);
            Assert.DoesNotContain(result.Trees, node => node.Label is "new FactoryGenerator" or "ServicePipelines.IsDefaultMiddleware");
            Assert.Contains(result.Trees, node => node.Label == "RegistrationExtensions.RegisterAssemblyOpenGenericTypes");
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("ResolvePipelineBuilder.BuildPipeline", root.Label);
        var loop = Assert.Single(root.Children, node => node.Label == "while (current is not null)");
        Assert.Equal("MiddlewareDeclaration.get_Middleware", loop.Children[0].Label);
        Assert.Equal("MiddlewareDeclaration.get_Previous", loop.Children[^1].Label);
        var removed = Assert.Single(loop.Children, node => node.Mark == '-');
        Assert.Equal("ResolvePipelineBuilder.BuildPipeline.BuildMiddlewareChain", removed.Label);
        var metricsGuard = Assert.Single(loop.Children, node => node.Mark == '+' && node.Label == "if (AutofacMetrics.MetricsEnabled)");
        var index = loop.Children.ToList().IndexOf(metricsGuard);
        Assert.Equal("AutofacMetrics.get_MetricsEnabled", loop.Children[index - 1].Label);
        VerifyBuilder(Assert.Single(metricsGuard.Children), true);
        var standardGuard = Assert.Single(loop.Children, node => node.Mark == '+' && node.Label == "else (!(AutofacMetrics.MetricsEnabled))");
        VerifyBuilder(Assert.Single(standardGuard.Children), false);
        Assert.Equal("new ResolvePipeline", root.Children[^1].Label);
    }

    private static void VerifyBuilder(DiffNode builder, bool metrics)
    {
        Assert.Equal("ResolvePipelineBuilder.Build" + (metrics ? "Metrics" : "Standard") + "MiddlewareChain", builder.Label);
        var phase = builder.Children[0];
        Assert.Equal("IResolveMiddleware.get_Phase", phase.Label);
        Assert.Equal("possible", phase.After!.Dispatch);
        Assert.Equal(10, phase.Children.Count);
        var callback = Assert.Single(builder.Children, node => node.Label == "callback");
        Assert.Equal("callback", callback.After!.Relation);
        Assert.Equal("ResolveRequestContext.get_DiagnosticSource → DefaultResolveRequestContext.get_DiagnosticSource", callback.Children[0].Label);
        var disabled = callback.Children[1];
        Assert.Equal("if (!context.DiagnosticSource.IsEnabled())", disabled.Label);
        Assert.Equal("ResolveRequestContext.set_PhaseReached → DefaultResolveRequestContext.set_PhaseReached", disabled.Children[0].Label);
        Assert.Equal(callback.Children[0].Label, callback.Children[2].Label);
        Assert.Equal("DiagnosticSourceExtensions.MiddlewareStart", callback.Children[3].Label);
        var execution = callback.Children[4];
        Assert.Equal("try", execution.Label);
        Assert.Equal(disabled.Children[0].Label, execution.Children[0].Label);
        if (metrics)
        {
            foreach (var path in new[] { disabled, execution })
            {
                Assert.Equal("ValueStopwatch.StartNew", path.Children[1].Label);
                var recording = Assert.Single(path.Children, node => node.Label == "finally");
                Assert.Equal(["ValueStopwatch.GetElapsedTime", "AutofacMetrics.RecordMiddlewareExecution"], recording.Children.Select(node => node.Label));
            }
        }
        var completion = callback.Children[^1];
        Assert.Equal("finally", completion.Label);
        Assert.Equal(["if (succeeded)", "else (!(succeeded))"], completion.Children.Select(node => node.Label));
        Assert.Equal(["DiagnosticSourceExtensions.MiddlewareSuccess", "DiagnosticSourceExtensions.MiddlewareFailure"],
            completion.Children.Select(node => node.Children[^1].Label));
        Assert.All(completion.Children, node => Assert.Equal(callback.Children[0].Label, node.Children[0].Label));
    }
}
