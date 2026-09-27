using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticInitializationIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task PartialDeclarationsPreserveWithinPartOrder(bool reverse) => VerifyPartialDeclarationOrder(false, reverse);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspacePartialDeclarationsPreserveWithinPartOrder(bool reverse) => VerifyPartialDeclarationOrder(true, reverse);

    private static async Task VerifyPartialDeclarationOrder(bool workspace, bool reverse)
    {
        var before = new Dictionary<string, string>
        {
            ["A.cs"] = "partial class State { public static int A1 = Sink.First(); public static int A2 = Sink.Second(); }",
            ["B.cs"] = "partial class State { public static int B1 = Sink.Third(); public static int B2 = Sink.Fourth(); }",
            ["Common.cs"] = "partial class State { static State() { Sink.Body(); } public static void Touch() {} } static class Entry { public static void Run() { State.Touch(); } } static class Sink { public static int First() => 1; public static int Second() => 2; public static int Third() => 3; public static int Fourth() => 4; public static void Body() {} }",
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"" + (reverse ? "B.cs;A.cs;Common.cs" : "A.cs;B.cs;Common.cs") + "\"/></ItemGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["A.cs"] = before["A.cs"].Replace("Sink.First()", "Sink.Second()", StringComparison.Ordinal) };
        var scenario = new Scenario("static-partial-order", "Partial declaration order is unspecified while each declaration retains its source order.", before, after, []);
        IReadOnlyDictionary<string, string> outputs;
        if (workspace)
        {
            await using var fixture = await GitFixture.CreateAsync(scenario);
            var rendered = new Dictionary<string, string>();
            foreach (var format in new[] { "json", "text", "md" })
            {
                string[] restore = format == "json" ? [] : ["--no-restore"];
                var output = await fixture.RunAsync(["tree", fixture.Before, "--entry", "Entry.Run", "--depth", "20", "--format", format, "--project", "App.csproj", .. restore]);
                Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
                rendered[format] = output;
            }
            outputs = rendered;
        }
        else
        {
            await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace: false);
            outputs = await fixture.QueryFormatsAsync(new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 }, before: true);
        }
        using var tree = workspace ? Parse(outputs["json"]) : JsonDocument.Parse(outputs["json"]);
        var nodes = Flatten(tree.RootElement.GetProperty("trees")).ToArray();
        var initializer = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "initialization of State");
        var children = initializer.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(3, children.Length);
        foreach (var item in new[] { (Index: 0, Path: "A.cs", Calls: new[] { "Sink.First", "Sink.Second" }), (Index: 1, Path: "B.cs", Calls: new[] { "Sink.Third", "Sink.Fourth" }) })
        {
            var part = children[item.Index];
            Assert.Equal("branch", part.GetProperty("kind").GetString());
            Assert.Equal("initializers in " + item.Path + " (order between parts unspecified)", part.GetProperty("label").GetString());
            Assert.Equal(item.Calls, part.GetProperty("children").EnumerateArray().Select(node => node.GetProperty("label").GetString()));
            Assert.Equal(item.Path, part.GetProperty("after").GetProperty("callSites")[0].GetProperty("path").GetString());
        }
        Assert.Equal("Sink.Body", children[2].GetProperty("label").GetString());
        var diagnostic = Assert.Single(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("static-initializer-order", diagnostic.GetProperty("code").GetString());
        foreach (var format in new[] { "text", "md" })
        {
            var output = outputs[format];
            Assert.Contains("order between parts unspecified", output);
            if (workspace) Assert.Contains("static-initializer-order", output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task SameAssemblyNamesRetainDeclaringProject(bool leftInitializer)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><AssemblyName>Shared</AssemblyName></PropertyGroup></Project>";
        var before = new Dictionary<string, string>
        {
            ["Left/Left.csproj"] = project,
            ["Right/Right.csproj"] = project,
            ["Left/Flow.cs"] = "public static class State { public static int Value" + (leftInitializer ? " = LeftSink.Before()" : "") + "; } public static class LeftSink { public static int Before() => 1; }",
            ["Right/Flow.cs"] = "public static class State { public static int Value = RightSink.Before(); } public static class RightSink { public static int Before() => 2; }",
            ["Consumer/Consumer.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Left/Left.csproj\"/></ItemGroup></Project>",
            ["Consumer/Flow.cs"] = "public static class Consumer { public static int Run() => State.Value; }",
            ["App.slnx"] = "<Solution><Project Path=\"Left/Left.csproj\"/><Project Path=\"Right/Right.csproj\"/><Project Path=\"Consumer/Consumer.csproj\"/></Solution>"
        };
        var after = new Dictionary<string, string>(before) { ["Left/Flow.cs"] = before["Left/Flow.cs"].Replace("Before() => 1", "Before() => 3", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("static-project-identity", "Field initialization retains its declaring project when independent projects share assembly and type names.", before, after, []));
        using var tree = Parse(await fixture.RunAsync(["tree", fixture.Before, "--entry", "Consumer.Run", "--solution", "App.slnx", "--depth", "20", "--format", "json"]));
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        var nodes = Flatten(tree.RootElement.GetProperty("trees")).ToArray();
        Assert.Equal(leftInitializer, nodes.Any(node => node.GetProperty("label").GetString() == "LeftSink.Before"));
        Assert.DoesNotContain(nodes, node => node.GetProperty("label").GetString() == "RightSink.Before");
        if (leftInitializer)
        {
            var initializer = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "initialization of State");
            Assert.Equal("project:Left/Left.csproj@net11.0::State..cctor()", initializer.GetProperty("after").GetProperty("symbolId").GetString());
            Assert.Equal("Left/Flow.cs", initializer.GetProperty("after").GetProperty("definition").GetProperty("path").GetString());
        }
        using var reach = Parse(await fixture.RunAsync(["reach", fixture.Before, "--entry", "Consumer.Run", "--to", "RightSink.Before", "--solution", "App.slnx", "--no-restore", "--depth", "20", "--format", "json"]));
        Assert.Empty(reach.RootElement.GetProperty("paths").EnumerateArray());
    }

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes) => nodes.EnumerateArray().SelectMany(node => new[] { node }.Concat(Flatten(node.GetProperty("children"))));
}
