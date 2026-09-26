using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class RecordCopyTests
{
    private const string Sink = " static class Sink { public static int Before() => 0; public static int After() => 0; public static int Other() => 0; }";
    private static readonly (string Name, string Source, bool ReachesChange)[] Fixtures =
    [
        ("sealed-copy", "sealed record State { public State() {} private State(State other) { Sink.Before(); } } static class Entry { public static void Run(State value) { _ = value with {}; } }", true),
        ("inherited-copy", "record Base { public Base() {} protected Base(Base other) { Sink.Before(); } } record State : Base; static class Entry { public static void Run(State value) { _ = value with {}; } }", true),
        ("abstract-dispatch", "abstract record Base { protected Base() {} protected Base(Base other) {} } record State : Base { public State() {} protected State(State other) : base(other) { Sink.Before(); } } static class Entry { public static void Run(Base value) { _ = value with {}; } }", true),
        ("generic-copy", "record Base<T> { public Base() {} protected Base(Base<T> other) { Sink.Before(); } } record State<T> : Base<T>; static class Entry { public static void Run(State<int> value) { _ = value with {}; } }", true),
        ("exact-receiver", "record Base { public Base() {} protected Base(Base other) { Sink.Other(); } } record State : Base { public State() {} protected State(State other) : base(other) { Sink.Before(); } } static class Entry { public static void Run() { _ = new Base() with {}; } }", false),
        ("field-copy-control", "record State { int field = Sink.Before(); } static class Entry { public static void Run(State value) { _ = value with {}; } }", false),
        ("struct-control", "record struct State { int field = Sink.Before(); public State() {} } static class Entry { public static void Run(State value) { _ = value with {}; } }", false),
        ("initializer-order", "record State { public int Value { get; init; } public State() {} protected State(State other) { Sink.Other(); } } static class Entry { public static void Run(State value) { _ = value with { Value = Sink.Before() }; } }", true),
        ("anonymous-control", "static class Entry { public static void Run() { var value = new { Number = 0 }; _ = value with { Number = Sink.Before() }; } }", true),
        ("cross-project", "record State : Base<int>; static class Entry { public static void Run(State value) { _ = value with {}; } }", true),
        ("direct-cross-project", "static class Entry { public static Base<int> Run(Base<int> value) => value with {}; }", true)
    ];

    public static IEnumerable<object[]> Cases => Fixtures.Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task FollowsRecordCopyCallsAcrossCommands(string name) => VerifyRecordCopy(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceFollowsRecordCopyCallsAcrossCommands(string name) => VerifyRecordCopy(name, true);

    private static async Task VerifyRecordCopy(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var before = new Dictionary<string, string> { ["Flow.cs"] = example.Source + Sink, ["App.csproj"] = project };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = example.Source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) + Sink };
        if (name is "cross-project" or "direct-cross-project")
        {
            before["App.csproj"] = after["App.csproj"] = project.Replace("</Project>", "<ItemGroup><Compile Remove=\"Base/**/*.cs\" /><ProjectReference Include=\"Base/Base.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal);
            before["Flow.cs"] = after["Flow.cs"] = example.Source;
            before["Base/Base.csproj"] = after["Base/Base.csproj"] = project;
            before["Base/Flow.cs"] = "public record Base<T> { public Base() {} protected Base(Base<T> other) { Sink.Before(); } } public static class Sink { public static int Before() => 0; public static int After() => 0; }";
            after["Base/Flow.cs"] = before["Base/Flow.cs"].Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal);
        }
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("record-copy", "Record copy construction preserves changed calls and excludes ordinary field initialization.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 16, IncludeExternals = true };
        var outputs = await fixture.DiffFormatsAsync(options, markdownAlias: true);
        foreach (var focused in new[] { false, true })
        {
            using var diff = Parse(focused ? outputs["json"] : await fixture.DiffAsync(options with { Entries = [] }));
            var roots = diff.RootElement.GetProperty("trees").EnumerateArray().Where(node => node.GetProperty("label").GetString() == "Entry.Run").ToArray();
            Assert.Equal(example.ReachesChange, roots.Length != 0);
            if (focused) Assert.Equal(example.ReachesChange, diff.RootElement.GetProperty("hasChanges").GetBoolean());
            if (example.ReachesChange)
            {
                var nodes = Flatten(Assert.Single(roots)).ToArray();
                Assert.Contains(nodes, node => node.GetProperty("label").GetString() == "Sink.Before" && node.GetProperty("change").GetString() == "removed");
                Assert.Contains(nodes, node => node.GetProperty("label").GetString() == "Sink.After" && node.GetProperty("change").GetString() == "added");
            }
        }
        using var tree = Parse(await fixture.QueryAsync(options, before: true));
        var calls = tree.RootElement.GetProperty("trees").EnumerateArray().SelectMany(Flatten).ToArray();
        Assert.Equal(example.ReachesChange, calls.Any(node => node.GetProperty("label").GetString() == "Sink.Before"));
        if (name == "direct-cross-project")
        {
            var clone = Assert.Single(calls, node => node.GetProperty("label").GetString()!.Contains("clone Base<T>", StringComparison.Ordinal));
            Assert.Equal("Base/Flow.cs", clone.GetProperty("after").GetProperty("definition").GetProperty("path").GetString());
            Assert.EndsWith("Base<T>.<Clone>$()", clone.GetProperty("after").GetProperty("symbolId").GetString());
        }
        if (name == "initializer-order")
        {
            Assert.Contains(calls, node => node.GetProperty("label").GetString() == "Sink.Other");
            Assert.True(Array.FindIndex(calls, node => node.GetProperty("label").GetString() == "Sink.Other") < Array.FindIndex(calls, node => node.GetProperty("label").GetString() == "Sink.Before"));
        }
        using var reach = Parse(await fixture.QueryAsync(options, before: true, target: "Sink.Before"));
        Assert.Equal(example.ReachesChange, reach.RootElement.GetProperty("paths").GetArrayLength() != 0);
        foreach (var format in new[] { "text", "markdown" })
        {
            var rendered = outputs[format];
            Assert.Equal(example.ReachesChange, rendered.Contains("Sink.Before", StringComparison.Ordinal));
            Assert.Equal(example.ReachesChange, rendered.Contains("Sink.After", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public async Task ReportsAmbiguousCopyConstructors(bool partial)
    {
        const string copy = "protected State(State other) { Sink.Before(); }";
        var source = partial ? "partial record State { public State() {} " + copy + " } partial record State { " + copy + " }"
            : "record State { public State() {} " + copy + " " + copy + " }";
        source += " static class Entry { public static void Run(State value) { _ = value with {}; } }" + Sink;
        var before = new Dictionary<string, string> { ["Flow.cs"] = source };
        var after = new Dictionary<string, string> { ["Flow.cs"] = source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("ambiguous-record-copy", "Ambiguous copy constructors retain diagnostics and omit expansion.", before, after, []), workspace: false);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["Entry.Run"], IncludeExternals = true }, before: true);
        using var document = JsonDocument.Parse(output);
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "unresolved-record-copy");
        Assert.DoesNotContain(document.RootElement.GetProperty("trees").EnumerateArray().SelectMany(Flatten), node => node.GetProperty("label").GetString() == "Sink.Before");
    }

    private static JsonDocument Parse(string output)
    {
        var document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        return document;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
