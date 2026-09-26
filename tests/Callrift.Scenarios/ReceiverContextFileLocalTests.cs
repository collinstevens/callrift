using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverContextFileLocalTests
{
    public static IEnumerable<object[]> Sources()
    {
        yield return ["override", "abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } file sealed class Worker : Base { protected override void Hook() => Sink.Run(); } static class Entry { public static void Run() => new Worker().Shared(); } static class Sink { public static void Run() {} }"];
        yield return ["empty-derived", "class Base { public void Shared() => Hook(); protected virtual void Hook() {} } class Other : Base { protected override void Hook() {} } file sealed class Worker : Base {} static class Entry { public static void Run() => new Worker().Shared(); }"];
        yield return ["nested", "abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } file class Outer { public sealed class Worker : Base { protected override void Hook() {} } } static class Entry { public static void Run() => new Outer.Worker().Shared(); }"];
        yield return ["generic-class", "file class Marker {} class Router<T> { public static void Run() {} } static class Entry { public static void Run() => Router<Marker>.Run(); }"];
        yield return ["generic-interface", "file interface IMarker {} class Router<T> { public static void Run() {} } static class Entry { public static void Run() => Router<IMarker>.Run(); }"];
    }

    public static IEnumerable<object[]> Modes() => Sources().SelectMany(source => new[] { false, true }.Select(workspace => new object[] { source[0], source[1], workspace }));

    [Theory]
    [MemberData(nameof(Sources))]
    [Trait("Layer", "Fast")]
    public void PhysicalPathsPreserveLogicalContext(string name, string source)
    {
        CallGraph Analyze(string directory)
        {
            var provider = new SourceOnlyAnalysisProvider();
            var trees = provider.Parse(new SourceSnapshot(name, [new SourceFile(directory + "/Flow.cs", source, source)]), new AnalysisOptions());
            var graph = SourceOnlyAnalysisProvider.AnalyzeCompilation(provider.CreateCompilation(trees), trees, logicalPath: Path.GetFileName);
            Assert.Empty(graph.Diagnostics);
            return graph;
        }
        var result = CallriftService.Compare(Analyze("before"), Analyze("after"), new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 16 });
        Assert.False(result.HasChanges);
        Assert.False(result.Truncated);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    [Trait("Layer", "Integration")]
    public async Task RevisionMaterializationPreservesFileLocalContexts(string name, string source, bool workspace)
    {
        var before = new Dictionary<string, string> { ["App.csproj"] = Project, ["Flow.cs"] = source };
        var after = new Dictionary<string, string>(before) { ["marker.txt"] = "after" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("receiver-file-local-" + name, "Unchanged file-local contexts survive revision materialization.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        using var result = Parse(await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--entry", "Entry.Run", "--depth", "16", .. mode, "--format", "json"]));
        Assert.Empty(result.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.False(result.RootElement.GetProperty("hasChanges").GetBoolean());
        Assert.False(result.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task SameBasenameReceiversKeepDistinctPaths() => VerifySameBasenameReceivers(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceSameBasenameReceiversKeepDistinctPaths() => VerifySameBasenameReceivers(true);

    private static async Task VerifySameBasenameReceivers(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = Project,
            ["Base.cs"] = "public abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } public static class Sink { public static void Left() {} public static void Right() {} }",
            ["First/Flow.cs"] = "file sealed class Worker : Base { protected override void Hook() => Sink.Left(); } public static class First { public static void Entry() => new Worker().Shared(); }",
            ["Second/Flow.cs"] = "file sealed class Worker : Base { protected override void Hook() => Sink.Right(); } public static class Second { public static void Entry() => new Worker().Shared(); }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Base.cs"] = before["Base.cs"].Replace("void Left() {}", "void Left() { throw new System.InvalidOperationException(); }", StringComparison.Ordinal)
        };
        var scenario = new Scenario("receiver-file-local-basename", "Same-basename files keep distinct file-local receivers through inherited helpers.", before, after, []);
        await using var fixture = workspace ? await AnalysisFixture.CreateWorkspaceCliAsync(scenario)
            : await AnalysisFixture.CreateAsync(scenario, workspace: false);
        using var first = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { Entries = ["First.Entry"], MaxDepth = 16 }));
        Assert.Empty(first.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.True(first.RootElement.GetProperty("hasChanges").GetBoolean());
        using var second = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { Entries = ["Second.Entry"], MaxDepth = 16 }));
        Assert.Empty(second.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.False(second.RootElement.GetProperty("hasChanges").GetBoolean());
        using var tree = JsonDocument.Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Second.Entry"], MaxDepth = 16 }));
        Assert.Contains("Sink.Right", tree.RootElement.GetProperty("trees").GetRawText());
        Assert.DoesNotContain("Sink.Left", tree.RootElement.GetProperty("trees").GetRawText());
    }

    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }
}
