using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogMetricsExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        if (workspace) Assert.Empty(result.Diagnostics);
        else Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        if (!focused)
        {
            Assert.Equal(workspace ? 184 : 135, result.Trees.Count);
            Assert.Equal(workspace ? 48 : 0, result.Trees.Count(node => node.Label.StartsWith("ILogger.", StringComparison.Ordinal)));
            Assert.Equal(workspace, result.Trees.Any(node => node.Label == "Log.CloseAndFlushAsync"));
            return;
        }
        Assert.Equal(5, result.Trees.Count);
        var initialization = Assert.Single(result.Trees, node => node.Label == "initialization of SelfMetrics");
        Assert.Equal('+', initialization.Mark);
        Assert.Null(initialization.Before);
        Assert.Equal("src/Serilog/Debugging/SelfMetrics.cs", initialization.After!.Definition!.Path);
        var creation = Assert.Single(initialization.Children, node => node.After?.SymbolId?.Contains(".Meter..ctor(string,string)", StringComparison.Ordinal) == true);
        Assert.Equal(7, Assert.Single(creation.After!.CallSites).Line);
        var counters = initialization.Children.Where(node => node.Label == "Meter.CreateCounter<long>").ToArray();
        Assert.Equal([17, 22, 27, 32], counters.Select(node => Assert.Single(node.After!.CallSites).Line));
        Assert.All(counters, node => Assert.Equal('+', node.Mark));
        var version = Assert.Single(initialization.Children, node => node.Kind == "branch");
        Assert.Equal("if (typeof(Log).Assembly.GetName().Version is not null)", version.Label);
        Assert.EndsWith("::System.Version.ToString()", Assert.Single(version.Children).After!.SymbolId);
        var dispatch = Assert.Single(result.Trees, node => node.Label == "Logger.Dispatch");
        AssertBefore(dispatch, "ILogEventSink.Emit", "SelfMetrics.PipelineEventEmitted.Add");
        AssertCounter(dispatch, "SelfMetrics.PipelineEventEmitted.Add", 483, false);
        var selfLog = Assert.Single(result.Trees, node => node.Label == "SelfLog.WriteLine");
        AssertBefore(selfLog, "if (o is not null)", "SelfMetrics.DiagnosticsSelfLogWrites.Add");
        AssertCounter(selfLog, "SelfMetrics.DiagnosticsSelfLogWrites.Add", 81, false);
        var failure = Assert.Single(result.Trees, node => node.Label == "SelfLog.SelfLogFailureListener.OnLoggingFailed");
        AssertBefore(failure, "kind.ToString", "TagList.Add");
        AssertBefore(failure, "TagList.Add", "SelfMetrics.DiagnosticsDefaultFailureListenerLoggingFailures.Add");
        AssertBefore(failure, "SelfMetrics.DiagnosticsDefaultFailureListenerLoggingFailures.Add", "possible initialization of SelfLog");
        AssertCounter(failure, "SelfMetrics.DiagnosticsDefaultFailureListenerLoggingFailures.Add", 95, true);
        var tag = Assert.Single(failure.Children, node => node.Label == "TagList.Add");
        Assert.EndsWith("::System.Diagnostics.TagList.Add(string,object)", tag.After!.SymbolId);
        Assert.Equal(92, Assert.Single(tag.After.CallSites).Line);
        Assert.DoesNotContain(failure.Children, node => node.Label == "if (o == null)");
        var configuration = Assert.Single(result.Trees, node => node.Label == "LoggerConfiguration.CreateLogger");
        AssertBefore(configuration, "SelfMetrics.PipelineCreated.Add", "new Logger");
        AssertCounter(configuration, "SelfMetrics.PipelineCreated.Add", 211, false);
        Assert.Equal(workspace, Descendants([configuration]).Any(node => node.Label == "LoggerConfiguration.CreateLogger.DisposeAsync"));
        foreach (var root in result.Trees.Where(node => node != initialization))
        {
            Assert.Equal(root.Before!.Signature, root.After!.Signature);
            var trigger = Assert.Single(root.Children, node => node.Label == "possible initialization of SelfMetrics");
            Assert.Equal('+', trigger.Mark);
            Assert.Equal("initialization of SelfMetrics", Assert.Single(trigger.Children).Label);
        }
    }

    private static void AssertCounter(DiffNode root, string label, int line, bool tagged)
    {
        var counter = Assert.Single(root.Children, node => node.Label == label);
        Assert.Equal('+', counter.Mark);
        Assert.Null(counter.Before);
        Assert.Equal("metadata", counter.After!.Origin);
        Assert.EndsWith(tagged ? ".Counter<T>.Add(T,in global::System.Diagnostics.TagList)" : ".Counter<T>.Add(T)", counter.After.SymbolId);
        Assert.Equal(line, Assert.Single(counter.After.CallSites).Line);
    }

    private static void AssertBefore(DiffNode root, string first, string second)
    {
        var labels = root.Children.Select(node => node.Label).ToList();
        Assert.Contains(first, labels);
        Assert.Contains(second, labels);
        Assert.True(labels.IndexOf(first) < labels.IndexOf(second));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
