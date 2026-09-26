using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticInitializationTests
{
    private static readonly (string Name, string Source, string Invoke, string Runtime, bool ReachesInitializer)[] Fixtures =
    {

    ("explicit-method-repeated", "static class State { static State() { Sink.Before(); } public static void Touch() => Sink.Add(\"body\"); }", "State.Touch(); State.Touch();", "before,body,body", true),
    ("explicit-instance", "class State { static State() { Sink.Before(); } public State() { Sink.Add(\"instance\"); } }", "_ = new State(); _ = new State();", "before,instance,instance", true),
    ("implicit-field-read", "static class State { public static int Value = Sink.Before(); }", "_ = State.Value;", "before", true),
    ("implicit-field-write", "static class State { public static int Value = Sink.Before(); }", "State.Value = 2;", "before", true),
    ("explicit-field-write-order", "static class State { public static int Value; static State() { Sink.Before(); } }", "State.Value = Sink.Argument();", "argument,before", true),
    ("explicit-method-argument-order", "static class State { static State() { Sink.Before(); } public static void Touch(int value) => Sink.Add(\"body\"); }", "State.Touch(Sink.Argument());", "argument,before,body", true),
    ("fields-before-constructor", "static class State { static int first = Sink.Before(); static int second = Sink.Argument(); static State() { Sink.Add(\"cctor\"); } public static void Touch() => Sink.Add(\"body\"); }", "State.Touch();", "before,argument,cctor,body", true),
    ("implicit-auto-property", "static class State { public static int Value { get; } = Sink.Before(); }", "_ = State.Value;", "before", true),
    ("explicit-getter", "static class State { static State() { Sink.Before(); } public static int Value => Sink.Argument(); }", "_ = State.Value;", "before,argument", true),
    ("explicit-setter-order", "static class State { static State() { Sink.Before(); } public static int Value { set { Sink.Add(\"setter\"); } } }", "State.Value = Sink.Argument();", "argument,before,setter", true),
    ("event-field-initializer", "static class State { public static event System.Action Changed = Create(); static System.Action Create() { Sink.Before(); return () => {}; } public static void Touch() { Changed(); } }", "State.Touch();", "before", true),
    ("explicit-event-add", "static class State { static State() { Sink.Before(); } public static event System.Action Changed { add { Sink.Add(\"add\"); } remove {} } }", "State.Changed += () => {};", "before,add", true),
    ("closed-generic-types", "static class State<T> { static State() { Sink.Before(); } public static void Touch() => Sink.Add(typeof(T).Name); }", "State<int>.Touch(); State<string>.Touch(); State<int>.Touch();", "before,Int32,before,String,Int32", true),
    ("inherited-static-owner", "class Base { static Base() { Sink.Before(); } public static void Touch() => Sink.Add(\"base-body\"); } class State : Base { static State() { Sink.Add(\"derived-init\"); } }", "State.Touch();", "before,base-body", true),
    ("self-reference", "static class State { public static int Value = Sink.Before(); static State() { Touch(); } public static void Touch() { _ = Value; Sink.Add(\"body\"); } }", "State.Touch();", "before,body,body", true),
    ("mutual-reference", "static class State { public static int Value = Sink.Before(); static State() { _ = Other.Value; Sink.Add(\"state\"); } } static class Other { public static int Value = Sink.Argument(); static Other() { _ = State.Value; Sink.Add(\"other\"); } }", "_ = State.Value;", "before,argument,other,state", true),
    ("constant-control", "static class State { static State() { Sink.Before(); } public const int Value = 1; }", "_ = State.Value;", "", false),
    ("typeof-control", "class State { static State() { Sink.Before(); } }", "_ = typeof(State);", "", false),
    ("nameof-control", "static class State { static State() { Sink.Before(); } public static int Value; }", "_ = nameof(State.Value);", "", false),
    ("default-struct-control", "struct State { static State() { Sink.Before(); } }", "_ = default(State);", "", false),
    ("struct-instance-method", "struct State { static State() { Sink.Before(); } public void Touch() => Sink.Add(\"body\"); }", "default(State).Touch();", "before,body", true),
    ("struct-default-construction-control", "struct State { static State() { Sink.Before(); } }", "_ = new State();", "", false),
    ("throwing-initializer", "static class State { static State() { Sink.Before(); throw new System.InvalidOperationException(); } public static void Touch() {} }", "try { State.Touch(); } catch (System.TypeInitializationException) { Sink.Add(\"caught\"); } try { State.Touch(); } catch (System.TypeInitializationException) { Sink.Add(\"caught\"); }", "before,caught,caught", true)
    };
    private const string Sink = " public static class Sink { public static readonly System.Collections.Generic.List<string> Events = new(); public static int Before() { Events.Add(\"before\"); return 1; } public static int After() { Events.Add(\"after\"); return 2; } public static int Argument() { Events.Add(\"argument\"); return 3; } public static void Add(string value) { Events.Add(value); } }";
    public static IEnumerable<object[]> Cases => Fixtures.Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task FollowsConditionalInitialization(string name) => VerifyConditionalInitialization(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceFollowsConditionalInitialization(string name) => VerifyConditionalInitialization(name, true);

    private static async Task VerifyConditionalInitialization(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var source = example.Source + " public static class Entry { public static void Run() { " + example.Invoke + " } }" + Sink;
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("static-" + name, "Conditional type initialization follows original declarations and closed generic contexts.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 30, IncludeExternals = true };
        using var tree = Parse(await fixture.QueryAsync(options, before: true));
        var calls = Flatten(tree.RootElement.GetProperty("trees")).ToArray();
        var labels = calls.Select(node => node.GetProperty("label").GetString()).ToArray();
        Assert.Equal(example.ReachesInitializer, labels.Contains("Sink.Before"));
        Assert.DoesNotContain(calls, node => node.TryGetProperty("omission", out var omission) && omission.ValueKind == JsonValueKind.Object && omission.GetProperty("reason").GetString() == "cycle");
        if (name is "explicit-method-repeated" or "explicit-instance" or "self-reference" or "mutual-reference") Assert.Single(labels, label => label == "Sink.Before");
        if (name == "closed-generic-types") Assert.Equal(2, labels.Count(label => label == "Sink.Before"));
        if (name is "explicit-method-repeated" or "self-reference") Assert.Equal(2, labels.Count(label => label == "State.Touch"));
        if (name is "explicit-field-write-order" or "explicit-method-argument-order") Assert.True(Array.IndexOf(labels, "Sink.Argument") < Array.IndexOf(labels, "Sink.Before"));
        if (name == "fields-before-constructor") Assert.True(Array.IndexOf(labels, "Sink.Before") < Array.IndexOf(labels, "Sink.Argument"));
        foreach (var initializer in calls.Where(node => node.GetProperty("label").GetString()!.StartsWith("initialization of ", StringComparison.Ordinal)))
        {
            var side = initializer.GetProperty("after");
            Assert.Contains("..cctor()", side.GetProperty("symbolId").GetString());
            Assert.NotEqual(JsonValueKind.Null, side.GetProperty("definition").ValueKind);
        }
        var outputs = await fixture.DiffFormatsAsync(options);
        using var diff = Parse(outputs["json"]);
        Assert.Equal(example.ReachesInitializer, diff.RootElement.GetProperty("hasChanges").GetBoolean());
        using var reach = Parse(await fixture.QueryAsync(options, before: true, target: "Sink.Before"));
        Assert.Equal(example.ReachesInitializer, reach.RootElement.GetProperty("paths").GetArrayLength() != 0);
        foreach (var format in new[] { "text", "md" })
        {
            var output = outputs[format];
            Assert.Equal(example.ReachesInitializer, output.Contains("Sink.Before", StringComparison.Ordinal));
            Assert.Equal(example.ReachesInitializer, output.Contains("Sink.After", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("method")]
    [InlineData("field")]
    [InlineData("generic-field")]
    [InlineData("inherited-method")]
    [Trait("Layer", "Fast")]
    public Task LinksInitializersAcrossSourceFiles(string kind) => VerifyInitializerLinks(kind, false);

    [Theory]
    [InlineData("method")]
    [InlineData("field")]
    [InlineData("generic-field")]
    [InlineData("inherited-method")]
    [Trait("Layer", "Integration")]
    public Task LinksInitializersAcrossProjects(string kind) => VerifyInitializerLinks(kind, true);

    private static async Task VerifyInitializerLinks(string kind, bool workspace)
    {
        var library = kind switch
        {
            "method" => "public static class State { static State() { Sink.Before(); } public static void Touch() {} }",
            "field" => "public static class State { public static int Value = Sink.Before(); }",
            "generic-field" => "public static class State<T> { public static int Value = Sink.Before(); }",
            _ => "public class Base { static Base() { Sink.Before(); } public static void Touch() {} }"
        };
        var invoke = kind switch
        {
            "field" => "_ = State.Value;",
            "generic-field" => "_ = State<int>.Value; _ = State<string>.Value; _ = State<int>.Value;",
            _ => "State.Touch();"
        };
        var source = (kind == "inherited-method" ? "public class State : Base {} " : "") + "public static class Entry { public static void Run() { " + invoke + " } }";
        var before = new Dictionary<string, string>
        {
            ["Lib/Flow.cs"] = library + Sink,
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><Compile Remove=\"Lib/**/*.cs\"/><ProjectReference Include=\"Lib/Lib.csproj\"/></ItemGroup></Project>",
            ["Lib/Lib.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Lib/Flow.cs"] = before["Lib/Flow.cs"].Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("static-project-" + kind, "Initialization uses the declaring project even when metadata imports hide its private constructor.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 30 };
        using var tree = Parse(await fixture.QueryAsync(options, before: true));
        var nodes = Flatten(tree.RootElement.GetProperty("trees")).ToArray();
        Assert.Equal(kind == "generic-field" ? 2 : 1, nodes.Count(node => node.GetProperty("label").GetString() == "Sink.Before"));
        foreach (var initializer in nodes.Where(node => node.GetProperty("label").GetString()!.StartsWith("initialization of ", StringComparison.Ordinal)))
        {
            var side = initializer.GetProperty("after");
            Assert.Equal("Lib/Flow.cs", side.GetProperty("definition").GetProperty("path").GetString());
            if (workspace) Assert.StartsWith("project:Lib/Lib.csproj@net11.0::", side.GetProperty("symbolId").GetString());
        }
        using var diff = Parse(await fixture.DiffAsync(options));
        Assert.True(diff.RootElement.GetProperty("hasChanges").GetBoolean());
        using var reach = Parse(await fixture.QueryAsync(options, before: true, target: "Sink.Before"));
        Assert.NotEmpty(reach.RootElement.GetProperty("paths").EnumerateArray());
    }

    private static JsonDocument Parse(string output)
    {
        var result = JsonDocument.Parse(output);
        Assert.Empty(result.RootElement.GetProperty("diagnostics").EnumerateArray());
        return result;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes) => nodes.EnumerateArray().SelectMany(node => new[] { node }.Concat(Flatten(node.GetProperty("children"))));
}
