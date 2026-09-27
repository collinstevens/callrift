using System.Runtime.Loader;
using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class EventAccessorTests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        "add", "remove", "static", "struct", "interface", "explicit-interface", "virtual", "base", "default-interface", "generic", "static-abstract", "expression-body", "conditional"
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task EventAccessorsReachTheirBodies(string name) => VerifyAccessor(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceEventAccessorsReachTheirBodies(string name) => VerifyAccessor(name, true);

    private static async Task VerifyAccessor(string name, bool workspace)
    {
        var (scenario, expected) = CreateScenario(name);
        Assert.Equal(expected, Execute(scenario.Before["Flow.cs"]));
        Assert.Equal(expected.Replace("before", "after", StringComparison.Ordinal), Execute(scenario.After["Flow.cs"]));
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20, Context = -1 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Sink.After" : null);
            foreach (var output in outputs.Values)
            {
                Assert.Contains("Entry.Run", output);
                Assert.Contains("Sink.After", output);
                Assert.DoesNotContain("Sink.Unrelated", output);
            }
            using var json = JsonDocument.Parse(outputs["json"]);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var nodes = Walk(json.RootElement.GetProperty("trees")).ToArray();
            var labels = nodes.Select(node => node.GetProperty("label").GetString()).ToArray();
            Assert.True(Array.IndexOf(labels, "Sink.Handler") < Array.IndexOf(labels, "Sink.After"));
            if (expected.StartsWith("receiver", StringComparison.Ordinal))
            {
                Assert.Single(labels, label => label == "Sink.Receiver");
                Assert.True(Array.IndexOf(labels, "Sink.Receiver") < Array.IndexOf(labels, "Sink.Handler"));
            }
            if (name == "conditional")
            {
                var branch = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "if (Receiver() is not null)");
                Assert.Contains(Walk(branch.GetProperty("children")), node => node.GetProperty("label").GetString() == "Sink.Handler");
                Assert.Contains(Walk(branch.GetProperty("children")), node => node.GetProperty("label").GetString() == "Sink.After");
            }
            if (name is "static" or "struct")
            {
                Assert.True(Array.IndexOf(labels, "Sink.Handler") < Array.IndexOf(labels, "Sink.Initialize"));
                Assert.True(Array.IndexOf(labels, "Sink.Initialize") < Array.IndexOf(labels, "Sink.After"));
            }
            var accessor = nodes.First(node => node.GetProperty("label").GetString()!.Contains(name == "remove" ? ".remove_Changed" : ".add_Changed", StringComparison.Ordinal));
            Assert.Contains("System.Action", accessor.GetProperty("after").GetProperty("symbolId").GetString());
            Assert.DoesNotContain(nodes, node => node.GetProperty("detail").GetString() == "cycle");
        }
        using var automatic = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { MaxDepth = 20 }));
        Assert.Contains(automatic.RootElement.GetProperty("trees").EnumerateArray(), node => node.GetProperty("label").GetString() == "Entry.Run");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliFindsEventAccessorChanges(bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario("interface").Scenario);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
            if (command == "diff") arguments.Add(fixture.After);
            arguments.AddRange(["--entry", "Entry.Run", "--format", "json"]);
            if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
            if (workspace) arguments.AddRange(["--project", "App.csproj"]);
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            Assert.Contains("Sink.After", output);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Integration")]
    public async Task EventInitializationDistinguishesCustomAccessorsFromFieldAccess(bool workspace, bool fieldLike)
    {
        var source = "static class Value { static int field = Initialize(); static int Initialize() { Sink.Before(); return 1; } public static event System.Action Changed"
            + (fieldLike ? ";" : " { add {} remove {} }")
            + " } static class Entry { public static void Run() { Value.Changed += () => {}; } } static class Sink { public static void Before() {} public static void After() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("event-initialization", "Generated event accessors touch their backing field; custom accessors do not imply a field read.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Sink.After" : null);
            Assert.Equal(fieldLike, output.Contains("Sink.After", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task MetadataEventsDistinguishSubscriptionFromUnsubscription(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() { System.Console.CancelKeyPress += OnCancel; } static void OnCancel(object sender, System.ConsoleCancelEventArgs args) {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("+=", "-=", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("metadata-event", "External event accessors keep distinct add and remove identities.", before, after, []), workspace);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions { Entries = ["Entry.Run"], IncludeExternals = true });
        foreach (var output in outputs.Values)
        {
            Assert.Contains("Console.add_CancelKeyPress", output);
            Assert.Contains("Console.remove_CancelKeyPress", output);
        }
        using var json = JsonDocument.Parse(outputs["json"]);
        Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        var nodes = Walk(json.RootElement.GetProperty("trees")).ToArray();
        var addition = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Console.add_CancelKeyPress").GetProperty("before");
        var removal = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Console.remove_CancelKeyPress").GetProperty("after");
        Assert.Equal("metadata", addition.GetProperty("origin").GetString());
        Assert.Equal("metadata", removal.GetProperty("origin").GetString());
        Assert.EndsWith("::System.Console.add_CancelKeyPress(global::System.ConsoleCancelEventHandler)", addition.GetProperty("symbolId").GetString());
        Assert.EndsWith("::System.Console.remove_CancelKeyPress(global::System.ConsoleCancelEventHandler)", removal.GetProperty("symbolId").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task AccessorsRetainTheDeclaringProject(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><Compile Remove=\"Lib/**/*.cs\"/><ProjectReference Include=\"Lib/Lib.csproj\"/></ItemGroup></Project>",
            ["Lib/Lib.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() { IEvents receiver = new Value(); receiver.Changed += () => {}; } }",
            ["Lib/Value.cs"] = "public interface IEvents { event System.Action Changed; } public class Value : IEvents { public event System.Action Changed { add { Sink.Before(); } remove {} } } public static class Sink { public static void Before() {} public static void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Lib/Value.cs"] = before["Lib/Value.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("event-project", "Event dispatch follows accessor bodies across project boundaries.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Sink.After" : null);
            Assert.Contains("Sink.After", output);
            using var json = JsonDocument.Parse(output);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var accessor = Assert.Single(Walk(json.RootElement.GetProperty("trees")), node => node.GetProperty("label").GetString() == "IEvents.add_Changed → Value.add_Changed");
            var side = accessor.GetProperty("after");
            Assert.Equal("Lib/Value.cs", side.GetProperty("definition").GetProperty("path").GetString());
            if (workspace) Assert.StartsWith("project:Lib/Lib.csproj@net11.0::", side.GetProperty("symbolId").GetString());
        }
    }

    private static (Scenario Scenario, string Runtime) CreateScenario(string name)
    {
        const string handlers = "public static System.Action Handler() { Sink.Handler(); return () => {}; }";
        const string body = "event System.Action Changed { add { Sink.Before(); } remove { Sink.Unrelated(); } }";
        var declaration = "class Value { public " + body + " }";
        var receiver = "static Value Receiver() { Sink.Receiver(); return new Value(); }";
        var invoke = "Receiver().Changed += Handler();";
        var expected = "receiver,handler,before";
        switch (name)
        {
            case "remove":
                declaration = "class Value { public event System.Action Changed { add { Sink.Unrelated(); } remove { Sink.Before(); } } }";
                invoke = "Receiver().Changed -= Handler();";
                break;
            case "static":
                declaration = "class Value { static Value() { Sink.Initialize(); } public static " + body + " }";
                invoke = "Value.Changed += Handler();";
                expected = "handler,initialize,before";
                break;
            case "struct":
                declaration = "struct Value { static Value() { Sink.Initialize(); } public " + body + " }";
                invoke = "Value value = default; value.Changed += Handler();";
                expected = "handler,initialize,before";
                break;
            case "interface":
            case "explicit-interface":
                declaration = "interface IEvents { event System.Action Changed; } class Value : IEvents { "
                    + (name == "interface" ? "public " + body : body.Replace("Changed", "IEvents.Changed", StringComparison.Ordinal)) + " }";
                receiver = "static IEvents Receiver() { Sink.Receiver(); return new Value(); }";
                break;
            case "virtual":
                declaration = "class Base { public virtual event System.Action Changed { add {} remove {} } } class Value : Base { public override " + body + " }";
                receiver = "static Base Receiver() { Sink.Receiver(); return new Value(); }";
                break;
            case "base":
                declaration = "class Base { public virtual " + body + " } class Value : Base { public override event System.Action Changed { add { Sink.Unrelated(); } remove {} } public void Attach() { base.Changed += Entry.Handler(); } }";
                invoke = "new Value().Attach();";
                expected = "handler,before";
                break;
            case "default-interface":
                declaration = "interface IEvents { " + body + " } class Value : IEvents {}";
                receiver = "static IEvents Receiver() { Sink.Receiver(); return new Value(); }";
                break;
            case "generic":
                declaration = "class Value<T> { public static " + body + " }";
                receiver = "";
                invoke = "Value<int>.Changed += Handler(); Value<string>.Changed += Handler();";
                expected = "handler,before,handler,before";
                break;
            case "static-abstract":
                declaration = "interface IEvents<T> where T : IEvents<T> { static IEvents() { Sink.Unrelated(); } static abstract event System.Action Changed; } class Value : IEvents<Value> { public static " + body + " } static class Adapter { public static void Attach<T>() where T : IEvents<T> { T.Changed += Entry.Handler(); } }";
                invoke = "Adapter.Attach<Value>();";
                expected = "handler,before";
                break;
            case "expression-body":
                declaration = "class Value { public event System.Action Changed { add => Sink.Before(); remove => Sink.Unrelated(); } }";
                break;
            case "conditional":
                invoke = "Receiver()?.Changed += Handler();";
                break;
        }
        var source = declaration + " public static class Entry { public static string Run() { " + invoke + " return string.Join(\",\", Sink.Trace); } " + receiver + handlers
            + " } public static class Sink { public static readonly System.Collections.Generic.List<string> Trace = new(); public static void Before() => Trace.Add(\"before\"); public static void After() => Trace.Add(\"after\"); public static void Handler() => Trace.Add(\"handler\"); public static void Receiver() => Trace.Add(\"receiver\"); public static void Initialize() => Trace.Add(\"initialize\"); public static void Unrelated() => Trace.Add(\"unrelated\"); }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        return (new Scenario("event-accessor-" + name, "Custom event subscriptions preserve receiver, handler, initialization and accessor order.", before, after, []), expected);
    }

    private static string Execute(string source)
    {
        var compilation = new SourceOnlyAnalysisProvider().CreateCompilation([CSharpSyntaxTree.ParseText(source)])
            .WithAssemblyName("EventAudit" + Guid.NewGuid().ToString("N"));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static IEnumerable<JsonElement> Walk(JsonElement nodes)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            yield return node;
            foreach (var child in Walk(node.GetProperty("children"))) yield return child;
        }
    }
}
