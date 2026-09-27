using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DepthReachabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task DepthHintsFollowSelectedDefaultInterfaceImplementations(bool inherited) => VerifyDepthHintsFollowSelectedDefaultInterfaceImplementations(inherited, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDepthHintsFollowSelectedDefaultInterfaceImplementations(bool inherited) => VerifyDepthHintsFollowSelectedDefaultInterfaceImplementations(inherited, true);

    private static async Task VerifyDepthHintsFollowSelectedDefaultInterfaceImplementations(bool inherited, bool workspace)
    {
        await using var fixture = await AnalysisFixture.CreateAsync(DefaultInterfaceScenario(inherited), workspace);
        foreach (var depth in new[] { 1, 8 })
        {
            var outputs = await fixture.DiffFormatsAsync(new DiffOptions { Entries = ["Entry.Run"], MaxDepth = depth });
            using var document = JsonDocument.Parse(outputs["json"]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(inherited, document.RootElement.GetProperty("hasChanges").GetBoolean());
            foreach (var format in new[] { "text", "md" })
                Assert.Equal(inherited, outputs[format].Contains("Entry.Run", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Integration")]
    public async Task CliDefaultInterfaceDispatchAgreesAcrossQueries(bool inherited, bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(DefaultInterfaceScenario(inherited));
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
            if (command == "diff") arguments.Add(fixture.After);
            arguments.AddRange(["--entry", "Entry.Run", "--depth", command == "diff" ? "1" : "8", "--format", "json"]);
            if (command == "reach") arguments.AddRange(["--to", "Changed.Run"]);
            if (workspace) arguments.AddRange(["--project", "App.csproj"]);
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command == "diff") Assert.Equal(inherited, document.RootElement.GetProperty("hasChanges").GetBoolean());
            else if (command == "reach") Assert.Equal(inherited, document.RootElement.GetProperty("paths").GetArrayLength() > 0);
            else Assert.Equal(inherited, output.Contains("Changed.Run", StringComparison.Ordinal));
        }
    }

    private static Scenario DefaultInterfaceScenario(bool inherited)
    {
        var source = """
            interface IWork { void Run() => Changed.Run(); }
            sealed class Worker : IWork { public void Run() => Preserved.Run(); }
            static class Entry { public static void Run(IWork worker) => Bridge.Run(worker); }
            static class Bridge { public static void Run(IWork worker) => worker.Run(); }
            static class Changed { public static void Run() => Sink.Before(); }
            static class Preserved { public static void Run() {} }
            static class Sink { public static void Before() {} public static void After() {} }
            """ + (inherited ? " sealed class InheritingWorker : IWork {}" : "");
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        return new Scenario("depth-default-interface", "Depth hints follow the same possible implementations as expanded calls.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) }, []);
    }

    [Fact]
    public void ConcurrentExpansionsRetainChangedDescendants()
    {
        var location = new SourceLocation("Flow.cs", 1, 1);
        var members = Enumerable.Range(0, 1024).ToDictionary(i => "Root" + i, i => new Member("Root" + i, "Root" + i, "Root" + i, "Root" + i, location, true,
            [new CallStep("call", "Child" + i, "Child" + i, true, location, [])]), StringComparer.Ordinal);
        foreach (var i in Enumerable.Range(0, 1024))
            members["Child" + i] = new Member("Child" + i, "Child" + i, "Child" + i, "Child" + i, location, true,
                [new CallStep("call", "Changed", "Changed", true, location, [])]);
        members["Changed"] = new Member("Changed", "Changed", "Changed", "Changed", location, true, []);
        var graph = new CallGraph(members, new Dictionary<string, IReadOnlyList<string>>(), []);
        for (var repeat = 0; repeat < 30; repeat++)
        {
            var expander = new TreeExpander(graph, new HashSet<string>(["Changed"], StringComparer.Ordinal), new DiffOptions { MaxDepth = 1 });
            var changedDescendants = new bool[1024];
            Parallel.For(0, changedDescendants.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 },
                i => changedDescendants[i] = expander.Expand("Root" + i).Children.Single().BodyChanged);
            Assert.All(changedDescendants, Assert.True);
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task ChangesRemainVisibleFromBothEntriesIntoACycle() => VerifyChangesRemainVisibleFromBothEntriesIntoACycle(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceChangesRemainVisibleFromBothEntriesIntoACycle() => VerifyChangesRemainVisibleFromBothEntriesIntoACycle(true);

    private static async Task VerifyChangesRemainVisibleFromBothEntriesIntoACycle(bool workspace)
    {
        const string source = """
            static class Entry
            {
                public static void First() => Cycle.A(true);
                public static void Second() => Cycle.B(false);
                public static void Unchanged() => Cycle.U(true);
            }
            static class Cycle
            {
                public static void A(bool recurse) { if (recurse) B(false); C(); }
                public static void B(bool recurse) => A(recurse);
                static void C() => Sink.Before();
                public static void U(bool recurse) { if (recurse) V(false); }
                static void V(bool recurse) => U(recurse);
            }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("depth-reachability-cycle", "Both entries into the cycle can reach the changed sibling call.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) }, []), workspace);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions { MaxDepth = 1 });
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = outputs[format];
            Assert.Contains("Entry.First", output);
            Assert.Contains("Entry.Second", output);
            Assert.DoesNotContain("Entry.Unchanged", output);
            if (format != "json") continue;
            using var document = JsonDocument.Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
            var roots = document.RootElement.GetProperty("trees").EnumerateArray().ToArray();
            Assert.Equal(new[] { "Entry.First", "Entry.Second" }, roots.Select(n => n.GetProperty("label").GetString()));
            foreach (var root in roots)
            {
                var call = Assert.Single(root.GetProperty("children").EnumerateArray());
                Assert.Equal("changes below depth limit", call.GetProperty("detail").GetString());
                Assert.Equal("depth-limit", call.GetProperty("omission").GetProperty("reason").GetString());
            }
        }
    }
}
