using System.Text;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverContextBoundaryTests
{
    [Fact]
    public async Task ReceiverStateLimitPreservesUnchangedCallers()
    {
        var source = new StringBuilder("abstract class Chain {");
        for (var index = 0; index < 255; index++) source.Append($"protected void Step{index}() => Step{index + 1}();");
        source.Append("protected void Step255() => Hook(); protected abstract void Hook(); }");
        for (var index = 0; index < 511; index++)
            source.Append($"sealed class Child{index:000} : Chain {{ public void Entry() => Step0(); protected override void Hook() {{}} }}");
        source.Append("sealed class Child511 : Chain { public void Added() {} protected override void Hook() {} }");
        var before = source.ToString();
        var after = before.Replace("public void Added() {}", "public void Added() => Step0();", StringComparison.Ordinal);
        var oldGraph = await Analyze(before);
        var newGraph = await Analyze(after);
        var result = CallriftService.Compare(oldGraph, newGraph, new DiffOptions { MaxDepth = 1 });
        Assert.Equal("Child511.Added", Assert.Single(result.Trees).Label);
        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "generic-context-limit");
        var selected = CallriftService.Compare(oldGraph, newGraph, new DiffOptions { Entries = ["Child500.Entry"], MaxDepth = 100 });
        Assert.False(selected.HasChanges);
        Assert.True(selected.Truncated);
    }

    private static async Task<CallGraph> Analyze(string content)
    {
        var graph = await new SourceOnlyAnalysisProvider().AnalyzeAsync(new SourceSnapshot("fixture", [new SourceFile("Flow.cs", content, content)]), new AnalysisOptions());
        Assert.Empty(graph.Diagnostics);
        return graph;
    }
}
