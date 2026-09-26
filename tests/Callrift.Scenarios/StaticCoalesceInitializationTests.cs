using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticCoalesceInitializationTests
{
    private static readonly (string Name, string Setup, string Left, int Initializers)[] Fixtures =
    [
        ("local-present", "object? value = new object();", "value", 2),
        ("local-null", "object? value = null;", "value", 2),
        ("static-field", "", "Holder.Shared", 2),
        ("instance-field", "var holder = new Holder();", "holder.Value", 2),
        ("array-element", "", "GetItems()[GetIndex()]", 2),
        ("already-initialized", "", "State.Value", 1)
    ];

    public static IEnumerable<object[]> Cases => Fixtures.Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task CoalescingAssignmentsPreserveConditionalInitialization(string name) => VerifyCoalescingAssignmentsPreserveConditionalInitialization(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceCoalescingAssignmentsPreserveConditionalInitialization(string name) => VerifyCoalescingAssignmentsPreserveConditionalInitialization(name, true);

    private static async Task VerifyCoalescingAssignmentsPreserveConditionalInitialization(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var source = """
            #nullable enable
            public static class Entry
            {
                private static readonly object?[] Items = [new object()];
                private static object?[] GetItems() { Sink.Receiver(); return Items; }
                private static int GetIndex() { Sink.Index(); return 0; }
                public static void Run() { SETUP LEFT ??= State.Make(); Sink.Marker(); State.Touch(); }
            }
            public class Holder { public static object? Shared = new object(); public object? Value = new object(); }
            public static class State
            {
                public static object? Value = new object();
                static State() { Sink.Before(); }
                public static object Make() { Sink.Make(); return new object(); }
                public static void Touch() { Sink.Touch(); }
            }
            public static class Sink { public static void Before() {} public static void After() {} public static void Marker() {} public static void Make() {} public static void Touch() {} public static void Receiver() {} public static void Index() {} }
            """.Replace("SETUP", example.Setup, StringComparison.Ordinal).Replace("LEFT", example.Left, StringComparison.Ordinal);
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("coalescing-initialization-" + name, "A skipped coalescing assignment preserves initialization on subsequent calls and evaluates its receiver once.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 15 };
        var outputs = await fixture.QueryFormatsAsync(options, before: true);
        using var tree = Parse(outputs["json"]);
        var root = Assert.Single(tree.RootElement.GetProperty("trees").EnumerateArray());
        var nodes = Descendants(root).ToArray();
        Assert.Equal(example.Initializers, nodes.Count(node => node.GetProperty("label").GetString() == "initialization of State"));
        var branchLabel = "if (" + example.Left + " is null)";
        var branch = Assert.Single(nodes, node => node.GetProperty("label").GetString() == branchLabel);
        Assert.Contains(Descendants(branch), node => node.GetProperty("label").GetString() == "State.Make");
        if (example.Initializers == 2)
        {
            var siblings = root.GetProperty("children").EnumerateArray().Select(node => node.GetProperty("label").GetString()).ToArray();
            Assert.True(Array.LastIndexOf(siblings, "possible initialization of State") > Array.IndexOf(siblings, "Sink.Marker"));
        }
        if (name == "array-element")
        {
            Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Entry.GetItems");
            Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Entry.GetIndex");
        }
        using var diff = Parse(await fixture.DiffAsync(options));
        Assert.True(diff.RootElement.GetProperty("hasChanges").GetBoolean());
        var changes = Descendants(Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray())).ToArray();
        Assert.Equal(example.Initializers, changes.Count(node => node.GetProperty("label").GetString() == "Sink.Before" && node.GetProperty("change").GetString() == "removed"));
        Assert.Equal(example.Initializers, changes.Count(node => node.GetProperty("label").GetString() == "Sink.After" && node.GetProperty("change").GetString() == "added"));
        using var reach = Parse(await fixture.QueryAsync(options, before: true, target: "Sink.Before"));
        Assert.NotEmpty(reach.RootElement.GetProperty("paths").EnumerateArray());
        foreach (var format in new[] { "text", "md" })
        {
            var output = outputs[format];
            Assert.Contains(branchLabel, output);
            Assert.Contains("State.Make", output);
            Assert.Contains("State.Touch", output);
        }
    }

    private static JsonDocument Parse(string output)
    {
        var result = JsonDocument.Parse(output);
        Assert.Empty(result.RootElement.GetProperty("diagnostics").EnumerateArray());
        return result;
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Descendants));
}
