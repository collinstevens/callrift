using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogSequencingExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        if (workspace) Assert.Empty(result.Diagnostics);
        else Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        if (!focused)
        {
            Assert.Equal(workspace ? 163 : 114, result.Trees.Count);
            Assert.Equal(workspace ? 48 : 0, result.Trees.Count(node => node.Label.StartsWith("ILogger.", StringComparison.Ordinal)));
            Assert.Equal(workspace, result.Trees.Any(node => node.Label == "Log.CloseAndFlushAsync"));
            Assert.Contains(result.Trees, node => node.Label == "LoggerAuditSinkConfiguration.Logger");
            Assert.Contains(result.Trees, node => node.Label == "Log.CloseAndFlush");
            return;
        }
        Assert.Equal(workspace ? 6 : 5, result.Trees.Count);
        var previous = Assert.Single(result.Trees, node => node.Label == "Logger.Dispatch");
        Assert.Equal('-', previous.Mark);
        Assert.Null(previous.After);
        Assert.Equal(["try", "catch (Exception ex)", "ILogEventSink.Emit", "possible initialization of SelfMetrics",
            "SelfMetrics.PipelineEventEmitted.Add"], previous.Children.Select(node => node.Label));
        Assert.Equal(483, Assert.Single(previous.Children[^1].Before!.CallSites).Line);
        var emission = Assert.Single(result.Trees, node => node.Label == "Logger.PostLevelCheckEmit");
        Assert.Equal('+', emission.Mark);
        Assert.Null(emission.Before);
        Assert.Equal(["try", "catch (Exception ex)", "ILogEventSink.Emit"], emission.Children.Select(node => node.Label));
        Assert.Equal("ILogEventEnricher.Enrich", Assert.Single(emission.Children[0].Children).Label);
        Assert.Equal("SelfLog.WriteLine", Assert.Single(emission.Children[1].Children).Label);
        var sink = emission.Children[2];
        Assert.Equal("possible", sink.After!.Dispatch);
        Assert.Equal(11, sink.After.TargetIds.Count);
        Assert.Equal(487, Assert.Single(sink.After.CallSites).Line);
        var explicitSink = Assert.Single(result.Trees, node => node.Label == "Logger.Serilog.Core.ILogEventSink.Emit");
        Assert.Equal(explicitSink.Before!.Signature, explicitSink.After!.Signature);
        Assert.Equal(["Guard.AgainstNull", "Logger.Dispatch", "Logger.PostLevelCheckEmit"], explicitSink.Children.Select(node => node.Label));
        Assert.DoesNotContain(Descendants(explicitSink.Children), node => node.After is not null && node.Label == "SelfMetrics.PipelineEventEmitted.Add");
        var writes = result.Trees.Where(node => node.Label == "Logger.Write").ToArray();
        Assert.Equal(workspace ? 3 : 2, writes.Length);
        foreach (var write in writes)
        {
            Assert.Equal(write.Before!.SymbolId, write.After!.SymbolId);
            Assert.Equal(write.Before.Signature, write.After.Signature);
            var suffix = write.Children.TakeLast(3).ToArray();
            Assert.Equal(["Logger.PostLevelCheckEmit", "possible initialization of SelfMetrics", "SelfMetrics.PipelineEventEmitted.Add"],
                suffix.Select(node => node.Label));
            Assert.All(suffix, node => Assert.Equal('+', node.Mark));
            Assert.Equal("initialization of SelfMetrics", Assert.Single(suffix[1].Children).Label);
            var counter = suffix[2];
            Assert.Equal("metadata", counter.After!.Origin);
            Assert.EndsWith(".Counter<T>.Add(T)", counter.After.SymbolId);
            var line = write.After.SymbolId!.EndsWith("(global::Serilog.Events.LogEvent)", StringComparison.Ordinal)
                ? 459 : write.After.SymbolId.Contains("ReadOnlySpan", StringComparison.Ordinal) ? 446 : 430;
            Assert.Equal(line, Assert.Single(counter.After.CallSites).Line);
            Assert.Equal(line - 1, Assert.Single(suffix[0].After!.CallSites).Line);
            Assert.DoesNotContain(write.Children, node => node.Label.Contains("!IsEnabled", StringComparison.Ordinal)
                || node.Label.Contains("logEvent == null", StringComparison.Ordinal)
                || node.Label.Contains("messageTemplate == null", StringComparison.Ordinal));
        }
        Assert.Equal(workspace, writes.Any(node => node.After!.Signature!.Contains("ReadOnlySpan<object?>", StringComparison.Ordinal)));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
