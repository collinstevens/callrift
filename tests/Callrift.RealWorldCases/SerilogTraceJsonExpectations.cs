using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogTraceJsonExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (workspace)
            Assert.Empty(result.Diagnostics);
        else
            Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        var root = Assert.Single(result.Trees);
        Assert.Equal("JsonFormatter.Format", root.Label);
        Assert.Equal(root.Before!.Signature, root.After!.Signature);
        VerifyGuard(root, "TraceId", 77, 77, focused, workspace);
        VerifyGuard(root, "SpanId", 83, 85, focused, workspace);
        Assert.Contains(root.Children, node => node.Label == "JsonValueFormatter.WriteQuotedJsonString" && node.Mark == ' ');
        var exception = Assert.Single(root.Children, node => node.Label == "if (logEvent.Exception != null)");
        Assert.Contains(exception.Children, node => node.Label == "JsonValueFormatter.WriteQuotedJsonString" && node.Mark == ' ');
    }

    private static void VerifyGuard(DiffNode root, string property, int previousLine, int addedLine, bool focused, bool workspace)
    {
        var guard = Assert.Single(root.Children, node => node.Label == $"if (logEvent.{property} != null)");
        Assert.Equal(' ', guard.Mark);
        var removed = Assert.Single(guard.Children, node => node.Label == "JsonValueFormatter.WriteQuotedJsonString");
        Assert.Equal('-', removed.Mark);
        Assert.Null(removed.After);
        Assert.Equal(previousLine, Assert.Single(removed.Before!.CallSites).Line);
        var getter = Assert.Single(guard.Children, node => node.Label == $"LogEvent.get_{property}");
        Assert.Equal(' ', getter.Mark);
        Assert.Equal(addedLine + 1, Assert.Single(getter.After!.CallSites).Line);
        if (!focused)
        {
            Assert.Equal(2, guard.Children.Count);
            return;
        }

        Assert.Equal(["output.Write", "output.Write", $"LogEvent.get_{property}", $"logEvent.{property}.ToString",
            "JsonValueFormatter.WriteQuotedJsonString", "output.Write", "output.Write"], guard.Children.Select(node => node.Label));
        var added = guard.Children.Where(node => node.Mark == '+').ToArray();
        Assert.Equal([addedLine, addedLine + 1, addedLine + 2], added.Select(node => Assert.Single(node.After!.CallSites).Line));
        Assert.Equal(["char", "string", "char"], added.Select(node => node.After!.SymbolId!.Split('(')[1].TrimEnd(')')));
        Assert.All(added, node =>
        {
            Assert.Null(node.Before);
            Assert.Equal("metadata", node.After!.Origin);
            Assert.Equal("resolved", node.After.Binding);
            Assert.Equal("direct", node.After.Dispatch);
        });
        var loop = Assert.Single(removed.Children, node => node.Label == "for (i < str.Length)");
        var escaping = Assert.Single(loop.Children, node => node.Kind == "branch");
        Assert.Contains(escaping.Children, node => node.Label == (workspace ? "str.AsSpan" : "str.Substring"));
        if (workspace)
        {
            var write = Assert.Single(escaping.Children, node => node.Label == "output.Write → ReusableStringWriter.Write");
            Assert.Equal("possible", write.Before!.Dispatch);
            Assert.Equal("depth-limit", write.Omission!.Reason);
        }
    }
}
