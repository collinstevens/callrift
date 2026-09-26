using System.Text;
using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class GenericContextBoundaryTests
{
    [Fact]
    public async Task RecursiveContextReference()
    {
        var graph = await Analyze("class Loop<T> { public static void Run() => Loop<string>.Run(); } static class Entry { public static void Run() => Loop<int>.Run(); }");
        var result = Query(graph, "Entry.Run", 12);
        var nodes = Flatten(result.Trees).ToArray();
        Require(nodes.Length == 4, "expected entry, int, string, string cycle");
        Require(nodes[^1].Omission?.Reason == "cycle", "missing closed-context cycle");
        using var json = JsonDocument.Parse(JsonRenderer.Render(result));
        var cycle = json.RootElement.GetProperty("trees")[0].GetProperty("children")[0].GetProperty("children")[0].GetProperty("children")[0];
        Require(cycle.GetProperty("omission").GetProperty("referenceId").GetString() == "n2", "cycle referred to a different instantiation");
        Require(!result.Truncated, "finite cycle must not be truncated");
    }

    [Fact]
    public async Task GrowingContextLimit()
    {
        var graph = await Analyze("class Loop<T> { public static void Run() => Loop<System.Collections.Generic.Dictionary<T, T>>.Run(); } static class Entry { public static void Run() => Loop<int>.Run(); }");
        var result = Query(graph, "Entry.Run", 100);
        Require(result.Truncated, "unbounded generic recursion was reported complete");
        Require(result.Diagnostics.Any(d => d.Code == "generic-context-limit"), "missing limit diagnostic");
        var nodes = Flatten(result.Trees).ToArray();
        Require(nodes.Any(n => n.Omission?.Reason == "generic-context-limit"), "missing visible limit omission");
        Require(nodes.All(n => n.Omission?.Reason != "cycle"), "growing contexts mislabeled as ordinary recursion");
        Require(JsonRenderer.Render(result) == JsonRenderer.Render(Query(graph, "Entry.Run", 100)), "context limit is nondeterministic");
    }

    [Fact]
    public async Task GenericArgumentOnlyChange()
    {
        const string source = "class Router { public static void Run<T>() {} } static class Entry { public static void Run() => Router.Run<int>(); }";
        var result = CallriftService.Compare(await Analyze(source), await Analyze(source.Replace("Run<int>()", "Run<string>()", StringComparison.Ordinal)), new DiffOptions());
        Require(result.Trees.Count == 1 && result.Trees[0].Label == "Entry.Run", "argument edit lost affected root");
        var call = result.Trees[0].Children.Single();
        Require(call.Detail == "generic arguments changed", "argument edit reported as declaration signature change");
        Require(call.Before?.SymbolId == call.After?.SymbolId, "argument edit changed external declaration identity");
    }

    [Fact]
    public async Task DispatchedArgumentOnlyChange()
    {
        const string source = "interface IHandler { void Run<T>(T value); } class Handler : IHandler { public void Run<U>(U value) {} } static class Entry { public static void Run(IHandler handler) => handler.Run(1L); }";
        var result = CallriftService.Compare(await Analyze(source), await Analyze(source.Replace("handler.Run(1L)", "handler.Run(\"text\")", StringComparison.Ordinal)), new DiffOptions());
        Require(result.Trees.Count == 1 && result.Trees[0].Label == "Entry.Run", "argument edit lost affected root");
        var call = result.Trees[0].Children.Single();
        Require(call.Detail == "generic arguments changed", "dispatched argument edit was mislabeled: " + call.Detail);
        Require(call.Before?.SymbolId == call.After?.SymbolId, "argument edit changed external declaration identity");
    }

    [Fact]
    public async Task AddedImplementationPreservesCallArguments()
    {
        const string source = "interface IHandler { void Run(); } class Original : IHandler { public void Run() {} } class Second : IHandler { public void Run() {} } static class Entry { public static void Run(IHandler handler) => handler.Run(); } class Unrelated<T> { public void Run() {} }";
        var result = CallriftService.Compare(await Analyze(source), await Analyze(source + " class Added : IHandler { public void Run() {} }"), new DiffOptions());
        var call = result.Trees.Single(t => t.Label == "Entry.Run").Children.Single();
        Require(call.Detail != "generic arguments changed", "adding an implementation did not change the call arguments");
        Require(call.Children.Any(child => child.Label.Contains("Added.Run", StringComparison.Ordinal) && child.Mark == '+'), "missing newly possible implementation");
    }

    [Fact]
    public async Task ConstraintOnlyContextChange()
    {
        const string source = """
            interface IHandler<T> { void Run(); }
            class Handler<T> : IHandler<T> { public void Run() => Sink.Call(); }
            class Router<T> { public static void Run(IHandler<T> handler) => handler.Run(); }
            static class Entry { public static void Number(IHandler<int> handler) => Router<int>.Run(handler); public static void Text(IHandler<string> handler) => Router<string>.Run(handler); }
            static class Sink { public static void Call() {} }
            """;
        var result = CallriftService.Compare(await Analyze(source), await Analyze(source.Replace("class Handler<T> : IHandler<T>", "class Handler<T> : IHandler<T> where T : class", StringComparison.Ordinal)), new DiffOptions());
        Require(result.Trees.Count == 1 && result.Trees[0].Label == "Entry.Number", "constraint edit affected compatible text caller: " + string.Join(",", result.Trees.Select(t => t.Label)));
    }

    [Fact]
    public async Task OpenEntryAndFileSelection()
    {
        const string source = """
            interface IHandler<T> { void Run(); }
            class Number : IHandler<int> { public void Run() => Sink.Number(); }
            class Text : IHandler<string> { public void Run() => Sink.Text(); }
            class Router<T> { public static void Run(IHandler<T> handler) => handler.Run(); }
            static class Entry { public static void Run(IHandler<int> handler) => Router<int>.Run(handler); }
            static class Sink { public static void Number() {} public static void Text() {} }
            """;
        var graph = await Analyze(source);
        var result = Query(graph, "Router<T>.Run", 12);
        Require(Flatten(result.Trees).Count(n => n.Label.StartsWith("Sink.", StringComparison.Ordinal)) == 2, "open generic entry lost possible implementations");
        var files = CallQueries.Query(graph, new QueryRequest("unused") { Options = new DiffOptions { Files = ["Flow.cs"], MaxDepth = 12 } });
        Require(files.Trees.Count == graph.Members.Count, "file selection duplicated specialized declarations");
    }

    [Fact]
    public async Task EquivalentContextRendering()
    {
        const string source = "class Router<T> { public static void Run() => Sink.Call(); } static class Entry { public static void Run() { Router<int>.Run(); Router<string>.Run(); } } static class Sink { public static void Call() {} }";
        var graph = await Analyze(source);
        var result = Query(graph, "Entry.Run", 12);
        var rendered = DiffRenderer.Render(result, new DiffOptions());
        Require(rendered.Contains("Router<T>.Run ×2", StringComparison.Ordinal), "identical visible calls were not grouped");
        using var json = JsonDocument.Parse(JsonRenderer.Render(result));
        Require(json.RootElement.GetProperty("trees")[0].GetProperty("children").GetArrayLength() == 2, "JSON must retain both invocation sites");
    }

    [Fact]
    public async Task DifferentContextRendering()
    {
        const string source = "interface IHandler<T> { void Run(); } class Number : IHandler<int> { public void Run() => Sink.Number(); } class Text : IHandler<string> { public void Run() => Sink.Text(); } class Router<T> { public static void Run(IHandler<T> handler) => handler.Run(); } static class Entry { public static void Run(IHandler<int> number, IHandler<string> text) { Router<int>.Run(number); Router<string>.Run(text); } } static class Sink { public static void Number() {} public static void Text() {} }";
        var result = Query(await Analyze(source), "Entry.Run", 12);
        var rendered = DiffRenderer.Render(result, new DiffOptions());
        Require(!rendered.Contains("Router<T>.Run ×2", StringComparison.Ordinal), "different downstream calls were grouped");
        Require(rendered.Contains("Sink.Number", StringComparison.Ordinal) && rendered.Contains("Sink.Text", StringComparison.Ordinal), "rendering lost a feasible context");
    }

    [Fact]
    public async Task NewClosedCallerPreservesEarlierOpenRoot()
    {
        const string source = """
            interface IHandler<T> { void Run(); }
            class Number : IHandler<int> { public void Run() {} }
            class Text : IHandler<string> { public void Run() => Sink.Before(); }
            class Router<T> { public static void Run(IHandler<T> handler) => handler.Run(); }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        var after = source.Replace("=> Sink.Before();", "=> Sink.After();", StringComparison.Ordinal)
            + " static class Entry { public static void Number(IHandler<int> handler) => Router<int>.Run(handler); }";
        var result = CallriftService.Compare(await Analyze(source), await Analyze(after), new DiffOptions());
        var open = result.Trees.Single(tree => tree.Label == "Router<T>.Run");
        Require(Flatten([open]).Any(node => node.Label == "Sink.After"), "the new integer caller hid the earlier open string-handler path");
        var closed = result.Trees.Single(tree => tree.Label == "Entry.Number");
        Require(Flatten([closed]).All(node => node.Label != "Sink.After"), "the integer caller acquired the string-handler change");
    }

    [Fact]
    public async Task SplitDeclarationCycle()
    {
        const string source = """
            interface IHandler<T> { void Run(); }
            class Number : IHandler<int> { public void Run() { Router<string>.Run(null); Sink.Before(); } }
            class Text : IHandler<string> { public void Run() {} }
            class Router<T> { public static void Run(IHandler<T> handler) => handler.Run(); }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        var result = CallriftService.Compare(await Analyze(source), await Analyze(source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal)), new DiffOptions());
        Require(result.Trees.Count > 0, "refined declaration SCC hid the changed implementation");
        Require(Flatten(result.Trees).Any(n => n.Label == "Sink.After"), "refined SCC root did not reach change");
    }

    static async Task<CallGraph> Analyze(string source)
    {
        var graph = await new SourceOnlyAnalysisProvider().AnalyzeAsync(new SourceSnapshot("fixture", [new SourceFile("Flow.cs", source, source)]), new AnalysisOptions());
        Require(graph.Diagnostics.Count == 0, JsonSerializer.Serialize(graph.Diagnostics));
        return graph;
    }
    static DiffResult Query(CallGraph graph, string entry, int depth) => CallQueries.Query(graph, new QueryRequest("unused") { Options = new DiffOptions { Entries = [entry], MaxDepth = depth } });
    static IEnumerable<DiffNode> Flatten(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; }
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
