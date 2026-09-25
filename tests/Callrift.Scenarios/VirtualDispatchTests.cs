using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class VirtualDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VirtualOverridesKeepBaseCallsDirect(bool workspace)
    {
        var original = ScenarioCatalog.All.Single(s => s.Name == "virtual-base");
        await using var fixture = await CreateAsync(original.Before["Program.cs"], original.After["Program.cs"]);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] selection = command == "diff" ? [] : ["--entry", "Flow.Handle"];
            string[] target = command == "reach" ? ["--to", "Derived.After"] : [];
            foreach (var format in new[] { "text", "md", "json" })
            {
                var output = await fixture.RunAsync([command, .. revisions, .. selection, .. target, .. mode, "--format", format, "--depth", "12", "--locs"]);
                Assert.StartsWith("exit: 0\n", output);
                Assert.Contains("Flow.Handle", output);
                Assert.Contains("Derived.After", output);
                Assert.DoesNotContain("cycle", output);
                if (command == "diff") Assert.Contains("Derived.Before", output);
                if (format != "json") continue;
                using var document = Parse(output);
                Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());
                var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
                Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
                var dispatch = Assert.Single(root.GetProperty("children").EnumerateArray());
                Assert.Equal("possible", dispatch.GetProperty("after").GetProperty("dispatch").GetString());
                Assert.Equal(["Derived.Run()", "Worker.Run()"], Targets(dispatch));
                if (command == "reach") continue;
                var implementation = dispatch.GetProperty("children").EnumerateArray().Single(n => n.GetProperty("label").GetString() == "⇢ Derived.Run");
                var baseCall = implementation.GetProperty("children")[0];
                Assert.Equal("Worker.Run", baseCall.GetProperty("label").GetString());
                Assert.Equal("direct", baseCall.GetProperty("after").GetProperty("dispatch").GetString());
                Assert.Equal(["Worker.Run()"], Targets(baseCall));
                Assert.Equal("Worker.Store", Assert.Single(baseCall.GetProperty("children").EnumerateArray()).GetProperty("label").GetString());
                Assert.Equal(8, baseCall.GetProperty("after").GetProperty("callSites")[0].GetProperty("startLine").GetInt32());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterfaceDispatchUsesMostDerivedGenericOverride(bool workspace)
    {
        const string source = """
            interface IWorker<T> { void Run(T value); }
            class Worker<T> : IWorker<T> { public virtual void Run(T value) {} }
            abstract class Middle<T> : Worker<T> { public override void Run(T value) { Before(); } void Before() {} void After() {} }
            sealed class Derived : Middle<string> {}
            class Hidden : Worker<string> { public new void Run(string value) {} }
            class Flow { public void Handle(IWorker<string> worker) => worker.Run("value"); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), splitProject: true);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        Assert.StartsWith("exit: 0\n", output);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
        var dispatch = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal(["Middle<T>.Run(T)", "Worker<T>.Run(T)"], Targets(dispatch));
        Assert.Contains("Middle<T>.After", output);
        Assert.DoesNotContain("Hidden.Run", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddedOverrideAffectsUneditedCallerButNotBaseMethodGroup(bool workspace)
    {
        const string before = """
            using System;
            class Worker { public virtual void Run() {} }
            class Derived : Worker { public void Direct() => Wrap(base.Run); void Wrap(Action callback) {} }
            class Flow { public void Handle(Worker worker) => worker.Run(); }
            """;
        var after = before.Replace("public void Direct()", "public override void Run() { Added(); } void Added() {} public void Direct()", StringComparison.Ordinal);
        await using var fixture = await CreateAsync(before, after);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        Assert.StartsWith("exit: 0\n", output);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
        Assert.Equal(["Derived.Run()", "Worker.Run()"], Targets(root.GetProperty("children")[0]));
        var direct = await fixture.RunAsync(["tree", fixture.After, "--entry", "Derived.Direct", .. mode, "--format", "json"]);
        Assert.StartsWith("exit: 0\n", direct);
        using var directDocument = Parse(direct);
        var callback = directDocument.RootElement.GetProperty("trees")[0].GetProperty("children")[0].GetProperty("children")[0];
        Assert.Equal("callback", callback.GetProperty("after").GetProperty("relation").GetString());
        Assert.Equal("direct", callback.GetProperty("after").GetProperty("dispatch").GetString());
        Assert.Equal(["Worker.Run()"], Targets(callback));
        Assert.DoesNotContain("Derived.Added", direct);
        var reach = await fixture.RunAsync(["reach", fixture.After, "--entry", "Derived.Direct", "--to", "Derived.Added", .. mode, "--format", "json"]);
        Assert.StartsWith("exit: 0\n", reach);
        using var reachDocument = Parse(reach);
        Assert.Empty(reachDocument.RootElement.GetProperty("paths").EnumerateArray());
        Assert.False(reachDocument.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactReceiversExcludeUnrelatedOverrides(bool workspace)
    {
        const string source = """
            class Worker { public virtual void Run() {} }
            class Derived : Worker { public override void Run() { Changed(); } void Changed() {} }
            sealed class Inherited : Worker { public void Implicit() => Run(); }
            class Flow { public void Handle(Inherited worker) { worker.Run(); worker?.Run(); new Worker().Run(); } }
            """;
        await using var fixture = await CreateAsync(source, source + " class Added {} ");
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var entry in new[] { "Flow.Handle", "Inherited.Implicit" })
        {
            var output = await fixture.RunAsync(["tree", fixture.After, "--entry", entry, .. mode, "--format", "json"]);
            Assert.StartsWith("exit: 0\n", output);
            using var document = Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Contains("Worker.Run", output);
            Assert.DoesNotContain("Derived.Run", output);
            Assert.DoesNotContain("\"dispatch\": \"possible\"", output);
        }
    }

    private static Task<GitFixture> CreateAsync(string before, string after, bool splitProject = false)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        Dictionary<string, string> Files(string source)
        {
            if (!splitProject) return new() { ["App.csproj"] = project, ["Flow.cs"] = source };
            var boundary = source.IndexOf("abstract class Middle", StringComparison.Ordinal);
            return new()
            {
                ["App.csproj"] = project.Replace("</Project>", "<ItemGroup><Compile Remove=\"Contracts/**/*.cs\"/><ProjectReference Include=\"Contracts/Contracts.csproj\"/></ItemGroup></Project>", StringComparison.Ordinal),
                ["Contracts/Contracts.csproj"] = project,
                ["Contracts/Contracts.cs"] = source[..boundary].Replace("interface IWorker", "public interface IWorker", StringComparison.Ordinal).Replace("class Worker", "public class Worker", StringComparison.Ordinal),
                ["Flow.cs"] = source[boundary..]
            };
        }
        return GitFixture.CreateAsync(new Scenario("virtual-dispatch", "Virtual overrides preserve possible targets and explicit base calls.",
            Files(before), Files(after), []));
    }

    private static JsonDocument Parse(string output) => JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);

    private static string[] Targets(JsonElement node) => node.GetProperty("after").GetProperty("targetIds").EnumerateArray()
        .Select(value => value.GetString()!.Split("::", StringSplitOptions.None)[1]).ToArray();
}
