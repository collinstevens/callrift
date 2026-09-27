using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class CollectionInitializerTests
{
    [Theory]
    [InlineData("instance")]
    [InlineData("extension")]
    [InlineData("nested")]
    [InlineData("nested-new")]
    [InlineData("exact")]
    [InlineData("callback")]
    [InlineData("generic")]
    [Trait("Layer", "Fast")]
    public Task InitializerCallsRemainReachable(string name) => VerifyInitializerCalls(name, false);

    [Theory]
    [InlineData("instance")]
    [InlineData("extension")]
    [InlineData("nested")]
    [InlineData("nested-new")]
    [InlineData("exact")]
    [InlineData("callback")]
    [InlineData("generic")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceInitializerCallsRemainReachable(string name) => VerifyInitializerCalls(name, true);

    private static async Task VerifyInitializerCalls(string name, bool workspace)
    {
        await using var fixture = await AnalysisFixture.CreateAsync(CreateScenario(name), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 8 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Sink.After" : null);
            foreach (var output in outputs.Values)
            {
                Assert.Contains("Entry.Run", output);
                Assert.Contains(".Add", output);
                Assert.Contains("Sink.After", output);
                if (name == "exact") Assert.DoesNotContain("OtherBag.Add", output);
                if (name == "generic") Assert.DoesNotContain("OtherWork.Run", output);
            }
            using var json = JsonDocument.Parse(outputs["json"]);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var children = json.RootElement.GetProperty("trees")[0].GetProperty("children").EnumerateArray().ToArray();
            if (name == "instance")
                Assert.Equal(["new Bag", "Value.Get", "Bag.Add", "Value.Get", "Value.Get", "Bag.Add"], children.Select(n => n.GetProperty("label").GetString()));
            if (name == "callback")
                Assert.Contains(Walk(children), n => n.GetProperty("after").GetProperty("relation").GetString() == "callback");
            foreach (var add in Walk(children).Where(n => n.GetProperty("kind").GetString() == "call" && n.GetProperty("label").GetString()!.EndsWith(".Add", StringComparison.Ordinal)))
                Assert.NotEmpty(add.GetProperty("after").GetProperty("callSites").EnumerateArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliFindsInitializerChanges(bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario("instance"));
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
            if (command == "diff") arguments.Add(fixture.After);
            arguments.AddRange(["--entry", "Entry.Run", "--format", "json"]);
            if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
            if (workspace) arguments.AddRange(["--project", "App.csproj"]);
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            Assert.Contains("Entry.Run", output);
            Assert.Contains("Bag.Add", output);
            Assert.Contains("Sink.After", output);
            Assert.DoesNotContain("unresolved-call", output);
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public async Task InvalidAddBindingRemainsVisibleForDelegateConstruction()
    {
        const string source = """
            using System.Collections;
            class Bag : IEnumerable { public void Add(int value) {} public IEnumerator GetEnumerator() => new int[0].GetEnumerator(); }
            static class Entry { public static void Run() { _ = new Bag { new System.Action(Callback) }; } static void Callback() {} }
            """;
        var graph = await new SourceOnlyAnalysisProvider().AnalyzeAsync(new SourceSnapshot("invalid", [new SourceFile("Flow.cs", source, source)]), new AnalysisOptions());
        var diagnostic = Assert.Single(graph.Diagnostics);
        Assert.Equal("unresolved-call", diagnostic.Code);
        Assert.Contains("collection initializer Add", diagnostic.Message);
        var output = JsonRenderer.Render(CallQueries.Query(graph, new QueryRequest("invalid") { Options = new DiffOptions { Entries = ["Entry.Run"] } }));
        Assert.Contains("? collection initializer Add", output);
        Assert.Contains("Entry.Callback", output);
    }

    private static IEnumerable<JsonElement> Walk(IEnumerable<JsonElement> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Walk(n.GetProperty("children").EnumerateArray())));

    private static Scenario CreateScenario(string name)
    {
        var (entry, types) = name switch
        {
            "instance" => ("new Bag { Value.Get(), { Value.Get(), Value.Get() } }", "class Bag : Enumerable { public void Add(int value) => Sink.Before(); public void Add(int key, int value) => Sink.Before(); }"),
            "extension" => ("new Bag { Value.Get() }", "class Bag : Enumerable {} static class Extensions { public static void Add<T>(this Bag bag, T value) => Sink.Before(); }"),
            "nested" => ("new Holder { Items = { Value.Get() } }", "class Holder { public Bag Items { get; } = new OtherBag(); } class Bag : Enumerable { public virtual void Add(int value) {} } class OtherBag : Bag { public override void Add(int value) => Sink.Before(); }"),
            "nested-new" => ("new Holder { Items = { new object() } }", "class Holder { public Bag Items { get; } = new OtherBag(); } class Bag : Enumerable { public virtual void Add(object value) {} } class OtherBag : Bag { public override void Add(object value) => Sink.Before(); }"),
            "exact" => ("new Bag { Value.Get() }", "class Bag : Enumerable { public virtual void Add(int value) => Sink.Before(); } class OtherBag : Bag { public override void Add(int value) {} }"),
            "callback" => ("new Bag { () => Sink.Before(), Sink.Before }", "class Bag : Enumerable { public void Add(System.Action action) => action(); }"),
            "generic" => ("new Bag<Work> { new Work() }", "class Bag<T> : Enumerable where T : IWork, new() { public void Add(T value) => new T().Run(); } interface IWork { void Run(); } class Work : IWork { public void Run() => Sink.Before(); } class OtherWork : IWork { public void Run() {} }"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var source = $$"""
            using System.Collections;
            static class Entry { public static void Run() { _ = {{entry}}; } }
            {{types}}
            class Enumerable : IEnumerable { public IEnumerator GetEnumerator() => new int[0].GetEnumerator(); }
            static class Value { public static int Get() => 1; }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        return new Scenario("collection-initializer-" + name, "Implicit collection initializer calls preserve binding, dispatch and argument evaluation.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before", "Sink.After", StringComparison.Ordinal) }, []);
    }
}
