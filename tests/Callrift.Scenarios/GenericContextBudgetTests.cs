using System.Text;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class GenericContextBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetBoundariesDoNotInventCallerEdits(bool shallow)
    {
        var steps = shallow ? 8 : 256;
        var entries = shallow ? 8300 : 260;
        var source = new StringBuilder("class Chain<T> {");
        for (var index = 0; index < steps - 1; index++) source.Append($"public static void Step{index}() => Step{index + 1}();");
        source.Append($"public static void Step{steps - 1}() {{}} }} static class Entry {{");
        for (var index = 0; index < entries; index++) source.Append($"public static void Existing{index:00000}() => Chain<Tag{index}>.Step0();");
        source.Append("public static void Added() {} }");
        for (var index = 0; index <= entries; index++) source.Append($"class Tag{index} {{}}");
        var before = source.ToString();
        var after = before.Replace("public static void Added() {}", $"public static void Added() => Chain<Tag{entries}>.Step0();", StringComparison.Ordinal);
        var provider = new SourceOnlyAnalysisProvider();
        var oldGraph = await provider.AnalyzeAsync(new SourceSnapshot("before", [new SourceFile("Flow.cs", before, "before")]), new AnalysisOptions());
        var newGraph = await provider.AnalyzeAsync(new SourceSnapshot("after", [new SourceFile("Flow.cs", after, "after")]), new AnalysisOptions());
        Assert.Empty(oldGraph.Diagnostics);
        Assert.Empty(newGraph.Diagnostics);
        var result = CallriftService.Compare(oldGraph, newGraph, new DiffOptions { MaxDepth = 1 });
        Assert.Equal("Entry.Added", Assert.Single(result.Trees).Label);
        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "generic-context-limit");
        if (shallow)
        {
            var selected = CallriftService.Compare(oldGraph, newGraph, new DiffOptions { Entries = ["Entry.Existing07421"], MaxDepth = 16 });
            Assert.False(selected.HasChanges);
            Assert.Empty(selected.Trees);
            Assert.True(selected.Truncated);
        }
    }
}
