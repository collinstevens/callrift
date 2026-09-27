using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstructorInitializationTests
{
    private const string entry = " static class Entry { public static void Run() { _ = new Derived(); } }";
    private const string sink = " static class Sink { public static int Before() => 0; public static int After() => 0; }";
    private static readonly (string Name, string Source, bool ReachesChange)[] Fixtures =
    [
        ("optional-base", "class Base { protected Base(int value = 3) { Sink.Before(); } } class Derived : Base {}" + entry, true),
        ("params-base", "class Base { protected Base(params int[] values) { Sink.Before(); } } class Derived : Base {}" + entry, true),
        ("optional-overload", "class Base { protected Base(int value = 3) { Sink.Before(); } protected Base(params string[] values) {} } class Derived : Base {}" + entry, true),
        ("closed-generic", "class Base<T> { protected Base(T value = default) { Sink.Before(); } } class Derived : Base<int> {}" + entry, true),
        ("primary", "class Base { protected Base(int value = 3) { Sink.Before(); } } class Derived() : Base {}" + entry, true),
        ("primary-explicit", "class Base { protected Base(int value) { Sink.Before(); } } class Derived() : Base(3) {}" + entry, true),
        ("partial", "class Base { protected Base(params int[] values) { Sink.Before(); } } partial class Derived : Base {} partial class Derived {}" + entry, true),
        ("abstract", "abstract class Base { int value = Sink.Before(); } class Derived : Base {}" + entry, true),
        ("this-chain", "class Base { protected Base() { Sink.Before(); } } class Derived : Base { public Derived() : this(3) {} public Derived(int value) {} }" + entry, true),
        ("partial-constructor", "class Base { protected Base() { Sink.Before(); } } partial class Derived : Base { public partial Derived(); public partial Derived() {} }" + entry, true),
        ("record-copy-base", "record Base { public Base() {} protected Base(Base other) { Sink.Before(); } } record Derived : Base { public static Derived Copy(Derived other) => new Derived(other); } static class Entry { public static void Run(Derived other) { _ = Derived.Copy(other); } }", true),
        ("record-copy-initializer", "record Derived { int value = Sink.Before(); public static Derived Copy(Derived other) => new Derived(other); } static class Entry { public static void Run(Derived other) { _ = Derived.Copy(other); } }", false),
        ("struct-default", "struct Derived { int value = Sink.Before(); public Derived(int value) {} }" + entry, false),
        ("struct-explicit", "struct Derived { int value = Sink.Before(); public Derived() {} }" + entry, true),
        ("struct-this", "struct Derived { int value = Sink.Before(); public Derived() : this(3) {} public Derived(int value) {} }" + entry, true),
        ("event-initializer", "class Derived { public event System.Action Changed = Create(); static System.Action Create() { Sink.Before(); return () => {}; } }" + entry, true),
        ("file-local", "class Base { protected Base() { Sink.Before(); } } file class Derived : Base {}" + entry, true),
        ("implicit-base", "class Base { protected Base() { Sink.Before(); } } class Derived : Base { public Derived() {} }" + entry, true),
        ("implicit-derived", "class Base { protected Base() { Sink.Before(); } } class Derived : Base {}" + entry, true),
        ("equivalent", "class Derived {}" + entry, false),
        ("cross-project", "class Derived : Base<int> {}" + entry, true)
    ];

    public static IEnumerable<object[]> Cases => Fixtures.Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task FollowsConstructorInitializationAcrossCommands(string name) => VerifyInitialization(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceFollowsConstructorInitializationAcrossCommands(string name) => VerifyInitialization(name, true);

    private static async Task VerifyInitialization(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>14.0</LangVersion></PropertyGroup></Project>";
        var before = new Dictionary<string, string> { ["Flow.cs"] = example.Source + sink, ["App.csproj"] = project };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = example.Source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) + sink };
        if (name == "equivalent")
            after["Flow.cs"] = example.Source.Replace("class Derived {}", "class Derived { public Derived() {} }", StringComparison.Ordinal) + sink;
        if (name == "cross-project")
        {
            var referencedProject = project;
            var applicationProject = project.Replace("</Project>", "<ItemGroup><Compile Remove=\"Base/**/*.cs\" /><ProjectReference Include=\"Base/Base.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal);
            before["App.csproj"] = after["App.csproj"] = applicationProject;
            before["Base/Base.csproj"] = after["Base/Base.csproj"] = referencedProject;
            before["Flow.cs"] = after["Flow.cs"] = example.Source;
            before["Base/Base.cs"] = "public class Base<T> { protected Base(T value = default) { Sink.Before(); } } public static class Sink { public static int Before() => 0; public static int After() => 0; }";
            after["Base/Base.cs"] = before["Base/Base.cs"].Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal);
        }
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("constructor-initialization", "Constructor calls and initializers retain their source-defined effects.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 14, IncludeExternals = true };
        foreach (var focused in new[] { false, true })
        {
            using var document = Parse(await fixture.DiffAsync(options with { Entries = focused ? ["Entry.Run"] : [] }));
            var roots = document.RootElement.GetProperty("trees").EnumerateArray().Where(node => node.GetProperty("label").GetString() == "Entry.Run").ToArray();
            Assert.Equal(example.ReachesChange, roots.Length != 0);
            if (focused) Assert.Equal(example.ReachesChange, document.RootElement.GetProperty("hasChanges").GetBoolean());
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
        using var reach = Parse(await fixture.QueryAsync(options, before: true, target: "Sink.Before"));
        Assert.Equal(example.ReachesChange, reach.RootElement.GetProperty("paths").GetArrayLength() != 0);
    }

    private static JsonDocument Parse(string output)
    {
        var document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        return document;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
