using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogRestrictedSinkExpectations
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
            Assert.Equal(workspace ? 13 : 12, result.Trees.Count);
            Assert.DoesNotContain(result.Trees, node => node.Label is "Log.Debug" or "Log.Information" or "Log.Warning" or "Log.Error" or "Log.Fatal" or "Log.Verbose");
            Assert.Equal(workspace, result.Trees.Any(node => node.Label == "Log.CloseAndFlushAsync"));
            foreach (var close in result.Trees.Where(node => node.Label is "Log.CloseAndFlush" or "Log.CloseAndFlushAsync"))
            {
                var none = Assert.Single(close.Children, node => node.Label == "Logger.get_None");
                Assert.Equal(' ', none.Mark);
                Assert.Equal("public static Serilog.Core.Logger.get_None() -> Serilog.ILogger", none.After!.Signature);
                Assert.DoesNotContain(close.Children, node => node.Label == "possible initialization of Logger");
            }
            return;
        }
        Assert.Equal(workspace ? 7 : 5, result.Trees.Count);
        var sink = Assert.Single(result.Trees, node => node.Label == "LoggerSinkConfiguration.Sink");
        Assert.Equal(sink.Before!.Signature, sink.After!.Signature);
        var nodes = Descendants([sink]).ToArray();
        Assert.Equal(2, nodes.Count(node => node.Label == "OptionalInterfaceForwardingSink.SupportsAll" && node.Mark == '+'));
        var conditions = nodes.Where(node => node.Label == "if (!OptionalInterfaceForwardingSink.SupportsAll(sink))").ToArray();
        Assert.Equal(2, conditions.Length);
        Assert.All(conditions, condition => Assert.Equal("OptionalInterfaceForwardingSink.SupportsAny", Assert.Single(condition.Children).Label));
        Assert.Equal(2, nodes.Count(node => node.Label == "new OptionalInterfaceForwardingSink" && node.Mark == '+'));
        Assert.Equal(2, nodes.Count(node => node.Label == "new RestrictedSink" && node.Mark == ' '));
        var removed = result.Trees.Where(node => node.Mark == '-').ToArray();
        Assert.Equal(workspace ? 3 : 2, removed.Length);
        Assert.All(removed, node =>
        {
            Assert.StartsWith("RestrictedSink.", node.Label);
            Assert.Null(node.After);
        });
        var dispatch = Descendants(result.Trees).Where(node => node.Kind == "dispatchTarget").ToArray();
        Assert.Contains(dispatch, node => node.Before?.SymbolId?.Contains(".RestrictedSink.Dispose()", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(dispatch, node => node.After?.SymbolId?.Contains(".RestrictedSink.", StringComparison.Ordinal) == true);
        Assert.Contains(Descendants(result.Trees), node => node.After?.Dispatch == "possible");
        Assert.Equal(workspace, result.Trees.Any(node => node.Label == "OptionalInterfaceForwardingSink.DisposeAsync"));
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
