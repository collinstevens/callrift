using System.Text.Json;
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

    public static IEnumerable<object[]> Cases => Fixtures.SelectMany(fixture => new[] { new object[] { fixture.Name, false }, new object[] { fixture.Name, true } });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReportsOnlyOmittedVisibleCallsAsTruncated(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var before = Files(example.Source);
        var after = Files(example.Source + " class Unrelated {} ");
        await using var fixture = await GitFixture.CreateAsync(new Scenario("depth-visibility", "A leaf at the depth boundary is complete when it has no visible calls.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var externals in new[] { false, true })
        {
            string[] visibility = externals ? ["--externals"] : [];
            var expected = externals ? example.WithExternals : example.WithoutExternals;
            using var tree = Parse(await fixture.RunAsync(["tree", fixture.Before, "--entry", "Entry.Run", "--depth", example.Depth.ToString(), "--format", "json", .. visibility, .. mode]));
            Assert.Equal(expected, tree.RootElement.GetProperty("truncated").GetBoolean());
            var nodes = tree.RootElement.GetProperty("trees").EnumerateArray().SelectMany(Flatten).ToArray();
            Assert.Equal(expected, nodes.Any(node => node.GetProperty("omission").ValueKind == JsonValueKind.Object && node.GetProperty("omission").GetProperty("reason").GetString() == "depth-limit"));
            if (name == "unresolved-child")
                Assert.Contains(tree.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("message").GetString()!.Contains("Missing", StringComparison.Ordinal));
            else
                Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
            foreach (var format in new[] { "text", "md" })
            {
                var output = await fixture.RunAsync(["tree", fixture.Before, "--entry", "Entry.Run", "--depth", example.Depth.ToString(), "--format", format, .. visibility, .. mode]);
                Assert.StartsWith("exit: 0\n", output);
                Assert.Equal(expected, output.Contains("depth limit", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesLeafBodyChangesAtTheDepthBoundary(bool workspace)
    {
        const string source = "static class Entry { public static int Run() => Leaf(); static int Leaf() => 1; }";
        await using var fixture = await GitFixture.CreateAsync(new Scenario("depth-body-change", "A changed leaf body remains visible without reporting omitted calls.", Files(source), Files(source.Replace("=> 1", "=> 2", StringComparison.Ordinal)), []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        using var diff = Parse(await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--entry", "Entry.Run", "--depth", "1", "--format", "json", .. mode]));
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
        Assert.StartsWith("exit: 0\n", output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
