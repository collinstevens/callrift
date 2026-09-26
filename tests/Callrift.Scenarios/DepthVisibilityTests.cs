using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DepthVisibilityTests
{
    private static readonly (string Name, string Source, bool WithoutExternals, bool WithExternals, int Depth)[] Fixtures =
    [
        ("struct-default", "struct State {} static class Entry { public static void Run() { _ = new State(); } }", false, false, 1),
        ("implicit-class", "class State {} static class Entry { public static void Run() { _ = new State(); } }", false, true, 1),
        ("empty-method", "static class Entry { public static void Run() => Leaf(); static void Leaf() {} }", false, false, 1),
        ("external-method", "static class Entry { public static void Run() => Leaf(); static void Leaf() { System.Console.WriteLine(); } }", false, true, 1),
        ("source-child", "static class Entry { public static void Run() => Leaf(); static void Leaf() { Sink(); } static void Sink() {} }", true, true, 1),
        ("unresolved-child", "static class Entry { public static void Run() => Leaf(); static void Leaf() { Missing(); } }", true, true, 1),
        ("hidden-branch", "static class Entry { public static void Run() => Leaf(); static void Leaf() { if (System.DateTime.Now.Ticks > 0) System.Console.WriteLine(); } }", false, true, 1),
        ("source-callback", "static class Entry { public static void Run() => Leaf(); static void Leaf() { System.Threading.Tasks.Task.Run(() => Sink()); } static void Sink() {} }", true, true, 1),
        ("dispatch-leaves", "interface ILeaf { void Work(); } class First : ILeaf { public void Work() {} } class Second : ILeaf { public void Work() {} } static class Entry { public static void Run(ILeaf value) => value.Work(); }", false, false, 2)
    ];

    public static IEnumerable<object[]> Cases => Fixtures.Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task ReportsOnlyOmittedVisibleCallsAsTruncated(string name) => VerifyReportsOnlyOmittedVisibleCallsAsTruncated(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceReportsOnlyOmittedVisibleCallsAsTruncated(string name) => VerifyReportsOnlyOmittedVisibleCallsAsTruncated(name, true);

    private static async Task VerifyReportsOnlyOmittedVisibleCallsAsTruncated(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var before = Files(example.Source);
        var after = Files(example.Source + " class Unrelated {} ");
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("depth-visibility", "A leaf at the depth boundary is complete when it has no visible calls.", before, after, []), workspace);
        foreach (var externals in new[] { false, true })
        {
            var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = example.Depth, IncludeExternals = externals };
            var outputs = await fixture.QueryFormatsAsync(options, before: true);
            var expected = externals ? example.WithExternals : example.WithoutExternals;
            using var tree = Parse(outputs["json"]);
            Assert.Equal(expected, tree.RootElement.GetProperty("truncated").GetBoolean());
            var nodes = tree.RootElement.GetProperty("trees").EnumerateArray().SelectMany(Flatten).ToArray();
            Assert.Equal(expected, nodes.Any(node => node.GetProperty("omission").ValueKind == JsonValueKind.Object && node.GetProperty("omission").GetProperty("reason").GetString() == "depth-limit"));
            if (name == "unresolved-child")
                Assert.Contains(tree.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("message").GetString()!.Contains("Missing", StringComparison.Ordinal));
            else
                Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
            foreach (var format in new[] { "text", "md" })
            {
                var output = outputs[format];
                Assert.Equal(expected, output.Contains("depth limit", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task PreservesLeafBodyChangesAtTheDepthBoundary() => VerifyPreservesLeafBodyChangesAtTheDepthBoundary(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspacePreservesLeafBodyChangesAtTheDepthBoundary() => VerifyPreservesLeafBodyChangesAtTheDepthBoundary(true);

    private static async Task VerifyPreservesLeafBodyChangesAtTheDepthBoundary(bool workspace)
    {
        const string source = "static class Entry { public static int Run() => Leaf(); static int Leaf() => 1; }";
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("depth-body-change", "A changed leaf body remains visible without reporting omitted calls.", Files(source), Files(source.Replace("=> 1", "=> 2", StringComparison.Ordinal)), []), workspace);
        using var diff = Parse(await fixture.DiffAsync(new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 1 }));
        Assert.True(diff.RootElement.GetProperty("hasChanges").GetBoolean());
        Assert.False(diff.RootElement.GetProperty("truncated").GetBoolean());
        var leaf = Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray().SelectMany(Flatten), node => node.GetProperty("label").GetString() == "Entry.Leaf");
        Assert.Equal("modified", leaf.GetProperty("change").GetString());
        Assert.Equal(JsonValueKind.Null, leaf.GetProperty("omission").ValueKind);
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    private static Dictionary<string, string> Files(string source) => new()
    {
        ["Flow.cs"] = source,
        ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
    };

    private static JsonDocument Parse(string output)
    {
        return JsonDocument.Parse(output);
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
