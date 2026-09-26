using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class GenericContextTests
{
    public static IEnumerable<object[]> Cases()
    {
        const string handlers = """
            interface IHandler<T> { void Run(T value); }
            class NumberHandler : IHandler<int> { public void Run(int value) => Sink.Number(); }
            class TextHandler : IHandler<string> { public void Run(string value) => Sink.Text(); }
            static class Sink { public static void Number() {} public static void Text() {} public static void Long() {} }
            """;
        var cases = new (string Name, string Source, string Entry, string[] Sinks)[]
        {
            ("nested-generic-type", handlers + """
                class Outer<T> { public class Inner<U> { public static void Run(IHandler<T> handler) => handler.Run(default); } }
                static class Entry { public static void Number(IHandler<int> handler) => Outer<int>.Inner<string>.Run(handler); }
                """, "Entry.Number", ["Sink.Number"]),
            ("generic-constructor", handlers + """
                class Router<T> { public Router(IHandler<T> handler) => handler.Run(default); }
                static class Entry { public static void Number(IHandler<int> handler) => new Router<int>(handler); }
                """, "Entry.Number", ["Sink.Number"]),
            ("primary-generic-base", handlers + """
                class Router<T> { public Router(IHandler<T> handler) => handler.Run(default); }
                class Derived<U>(IHandler<U> handler) : Router<U>(handler);
                static class Entry { public static void Number(IHandler<int> handler) => new Derived<int>(handler); }
                """, "Entry.Number", ["Sink.Number"]),
            ("generic-method-group", handlers + """
                class Router<T> { static IHandler<T> handler; public static void Run() => handler.Run(default); }
                static class Entry { public static void Number() => Schedule(Router<int>.Run); public static void Schedule(System.Action action) {} }
                """, "Entry.Number", ["Sink.Number"]),
            ("generic-conditional-access", handlers + """
                class Router<T> { readonly IHandler<T> handler; public Router(IHandler<T> handler) { this.handler = handler; } public void Run(T value) => handler?.Run(value); }
                static class Entry { public static void Number(Router<int> router) => router?.Run(1); }
                """, "Entry.Number", ["Sink.Number"]),
            ("nested-local-generic-method", handlers + """
                static class Router { public static void Run<T>(IHandler<T> handler) { void Local<U>(U value) => handler.Run(default); Local(1L); } }
                static class Entry { public static void Number(IHandler<int> handler) => Router.Run(handler); }
                """, "Entry.Number", ["Sink.Number"]),
            ("closed-class-number", handlers + """
                class Router<T> { readonly IHandler<T> handler; public Router(IHandler<T> handler) { this.handler = handler; } public void Run(T value) => handler.Run(value); }
                static class Entry { public static void Number(Router<int> router) => router.Run(1); public static void Text(Router<string> router) => router.Run("text"); }
                """, "Entry.Number", ["Sink.Number"]),
            ("closed-class-text", handlers + """
                class Router<T> { readonly IHandler<T> handler; public Router(IHandler<T> handler) { this.handler = handler; } public void Run(T value) => handler.Run(value); }
                static class Entry { public static void Number(Router<int> router) => router.Run(1); public static void Text(Router<string> router) => router.Run("text"); }
                """, "Entry.Text", ["Sink.Text"]),
            ("generic-method", handlers + """
                static class Router { public static void Run<T>(IHandler<T> handler, T value) => handler.Run(value); }
                static class Entry { public static void Number(IHandler<int> handler) => Router.Run(handler, 1); }
                """, "Entry.Number", ["Sink.Number"]),
            ("reduced-extension", handlers + """
                static class Router { public static void Route<T>(this IHandler<T> handler, T value) => handler.Run(value); }
                static class Entry { public static void Number(IHandler<int> handler) => handler.Route(1); }
                """, "Entry.Number", ["Sink.Number"]),
            ("callback", handlers + """
                static class Router { public static void Run<T>(IHandler<T> handler, T value) => Schedule(() => handler.Run(value)); public static void Schedule(System.Action action) {} }
                static class Entry { public static void Number(IHandler<int> handler) => Router.Run(handler, 1); }
                """, "Entry.Number", ["Sink.Number"]),
            ("local-function", handlers + """
                static class Router { public static void Run<T>(IHandler<T> handler, T value) { void Local() => handler.Run(value); Local(); } }
                static class Entry { public static void Number(IHandler<int> handler) => Router.Run(handler, 1); }
                """, "Entry.Number", ["Sink.Number"]),
            ("inherited-implementation", """
                interface IHandler<T> { void Run(T value); }
                interface ISink<T> { void Call(); }
                class Base<U> : IHandler<U> { readonly ISink<U> sink; public Base(ISink<U> sink) { this.sink = sink; } public void Run(U value) => sink.Call(); }
                class Derived<T> : Base<T> { public Derived(ISink<T> sink) : base(sink) {} }
                class NumberSink : ISink<int> { public void Call() => Sink.Number(); }
                class TextSink : ISink<string> { public void Call() => Sink.Text(); }
                static class Sink { public static void Number() {} public static void Text() {} }
                static class Entry { public static void Number(IHandler<int> handler) => handler.Run(1); }
                """, "Entry.Number", ["Sink.Number"]),
            ("generic-interface-method", """
                interface IHandler<T> { void Run<U>(U value); }
                interface ISink<T> { void Call(); }
                class Handler<T> : IHandler<T> { public void Run<V>(V value) => Router<V>.Run(); }
                class Router<T> { static ISink<T> sink; public static void Run() => sink.Call(); }
                class LongSink : ISink<long> { public void Call() => Sink.Long(); }
                class TextSink : ISink<string> { public void Call() => Sink.Text(); }
                static class Sink { public static void Long() {} public static void Text() {} }
                static class Entry { public static void Number(IHandler<int> handler) => handler.Run(1L); }
                """, "Entry.Number", ["Sink.Long"])
        };
        foreach (var item in cases)
            yield return [item.Name, item.Source, item.Entry, item.Sinks];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task CallsRetainGenericInvocationArguments(string name, string source, string entry, string[] sinks) =>
        VerifyInvocationArguments(name, source, entry, sinks, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceCallsRetainGenericInvocationArguments(string name, string source, string entry, string[] sinks) =>
        VerifyInvocationArguments(name, source, entry, sinks, true);

    private static async Task VerifyInvocationArguments(string name, string source, string entry, string[] sinks, bool workspace)
    {
        var before = source.Replace("public static void Text() {}", "public static void Text() {} public static void After() {}", StringComparison.Ordinal);
        var after = before.Replace("=> Sink.Text();", "=> Sink.After();", StringComparison.Ordinal);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("generic-context-" + name, "Generic invocation arguments constrain possible downstream dispatch.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = before },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []), workspace);
        var options = new DiffOptions { Entries = [entry], MaxDepth = 16 };
        var output = await fixture.QueryAsync(options, before: true);
        using var tree = JsonDocument.Parse(output);
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.DoesNotContain("\\u001e", output, StringComparison.OrdinalIgnoreCase);
        var reached = Flatten(tree.RootElement.GetProperty("trees")).Select(n => n.GetProperty("label").GetString()!)
            .Where(label => label.StartsWith("Sink.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(sinks, reached);
        foreach (var sink in sinks)
        {
            using var reach = JsonDocument.Parse(await fixture.QueryAsync(options, before: true, target: sink));
            Assert.Empty(reach.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Single(reach.RootElement.GetProperty("paths").EnumerateArray());
        }
        var excluded = sinks.Contains("Sink.Text", StringComparer.Ordinal) ? "Sink.Number" : "Sink.Text";
        using var absent = JsonDocument.Parse(await fixture.QueryAsync(options, before: true, target: excluded));
        Assert.Empty(absent.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Empty(absent.RootElement.GetProperty("paths").EnumerateArray());
        using var diff = JsonDocument.Parse(await fixture.DiffAsync());
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        var expected = name switch
        {
            "closed-class-number" or "closed-class-text" => "Entry.Text",
            "inherited-implementation" or "generic-interface-method" => "TextSink.Call",
            _ => "TextHandler.Run"
        };
        Assert.Equal(expected, Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            yield return node;
            foreach (var child in Flatten(node.GetProperty("children"))) yield return child;
        }
    }

}
