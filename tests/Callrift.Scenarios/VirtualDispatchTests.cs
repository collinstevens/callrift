using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class VirtualDispatchTests
{
    [Fact]
    [Trait("Layer", "Fast")]
    public Task VirtualOverridesKeepBaseCallsDirect() => VerifyVirtualOverridesKeepBaseCallsDirect(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceVirtualOverridesKeepBaseCallsDirect() => VerifyVirtualOverridesKeepBaseCallsDirect(true);

    private static async Task VerifyVirtualOverridesKeepBaseCallsDirect(bool workspace)
    {
        var original = ScenarioCatalog.All.Single(s => s.Name == "virtual-base");
        await using var fixture = await CreateAsync(original.Before["Program.cs"], original.After["Program.cs"], workspace);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var options = new DiffOptions { Entries = command == "diff" ? [] : ["Flow.Handle"], MaxDepth = 12, Locations = true };
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Derived.After" : null);
            foreach (var format in new[] { "text", "md", "json" })
            {
                var output = outputs[format];
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

    [Fact]
    [Trait("Layer", "Fast")]
    public Task InterfaceDispatchUsesMostDerivedGenericOverride() => VerifyInterfaceDispatchUsesMostDerivedGenericOverride(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceInterfaceDispatchUsesMostDerivedGenericOverride() => VerifyInterfaceDispatchUsesMostDerivedGenericOverride(true);

    private static async Task VerifyInterfaceDispatchUsesMostDerivedGenericOverride(bool workspace)
    {
        const string source = """
            interface IWorker<T> { void Run(T value); }
            class Worker<T> : IWorker<T> { public virtual void Run(T value) {} }
            abstract class Middle<T> : Worker<T> { public override void Run(T value) { Before(); } void Before() {} void After() {} }
            sealed class Derived : Middle<string> {}
            class Hidden : Worker<string> { public new void Run(string value) {} }
            class Flow { public void Handle(IWorker<string> worker) => worker.Run("value"); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal), workspace, splitProject: true);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
        var dispatch = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal(["Middle<T>.Run(T)", "Worker<T>.Run(T)"], Targets(dispatch));
        Assert.Contains("Middle<T>.After", output);
        Assert.DoesNotContain("Hidden.Run", output);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task AddedOverrideAffectsUneditedCallerButNotBaseMethodGroup() => VerifyAddedOverrideAffectsUneditedCallerButNotBaseMethodGroup(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceAddedOverrideAffectsUneditedCallerButNotBaseMethodGroup() => VerifyAddedOverrideAffectsUneditedCallerButNotBaseMethodGroup(true);

    private static async Task VerifyAddedOverrideAffectsUneditedCallerButNotBaseMethodGroup(bool workspace)
    {
        const string before = """
            using System;
            class Worker { public virtual void Run() {} }
            class Derived : Worker { public void Direct() => Wrap(base.Run); void Wrap(Action callback) {} }
            class Flow { public void Handle(Worker worker) => worker.Run(); }
            """;
        var after = before.Replace("public void Direct()", "public override void Run() { Added(); } void Added() {} public void Direct()", StringComparison.Ordinal);
        await using var fixture = await CreateAsync(before, after, workspace);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
        Assert.Equal(["Derived.Run()", "Worker.Run()"], Targets(root.GetProperty("children")[0]));
        var direct = await fixture.QueryAsync(new DiffOptions { Entries = ["Derived.Direct"] });
        using var directDocument = Parse(direct);
        var callback = directDocument.RootElement.GetProperty("trees")[0].GetProperty("children")[0].GetProperty("children")[0];
        Assert.Equal("callback", callback.GetProperty("after").GetProperty("relation").GetString());
        Assert.Equal("direct", callback.GetProperty("after").GetProperty("dispatch").GetString());
        Assert.Equal(["Worker.Run()"], Targets(callback));
        Assert.DoesNotContain("Derived.Added", direct);
        var reach = await fixture.QueryAsync(new DiffOptions { Entries = ["Derived.Direct"] }, target: "Derived.Added");
        using var reachDocument = Parse(reach);
        Assert.Empty(reachDocument.RootElement.GetProperty("paths").EnumerateArray());
        Assert.False(reachDocument.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task ExactReceiversExcludeUnrelatedOverrides() => VerifyExactReceiversExcludeUnrelatedOverrides(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceExactReceiversExcludeUnrelatedOverrides() => VerifyExactReceiversExcludeUnrelatedOverrides(true);

    private static async Task VerifyExactReceiversExcludeUnrelatedOverrides(bool workspace)
    {
        const string source = """
            class Worker { public virtual void Run() {} }
            class Derived : Worker { public override void Run() { Changed(); } void Changed() {} }
            sealed class Inherited : Worker { public void Implicit() => Run(); }
            class Flow { public void Handle(Inherited worker) { worker.Run(); worker?.Run(); new Worker().Run(); } }
            """;
        await using var fixture = await CreateAsync(source, source + " class Added {} ", workspace);
        foreach (var entry in new[] { "Flow.Handle", "Inherited.Implicit" })
        {
            var output = await fixture.QueryAsync(new DiffOptions { Entries = [entry] });
            using var document = Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Contains("Worker.Run", output);
            Assert.DoesNotContain("Derived.Run", output);
            Assert.DoesNotContain("\"dispatch\": \"possible\"", output);
        }
    }

    private static Task<AnalysisFixture> CreateAsync(string before, string after, bool workspace, bool splitProject = false)
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
        return AnalysisFixture.CreateAsync(new Scenario("virtual-dispatch", "Virtual overrides preserve possible targets and explicit base calls.",
            Files(before), Files(after), []), workspace);
    }

    private static JsonDocument Parse(string output) => JsonDocument.Parse(output);

    private static string[] Targets(JsonElement node) => node.GetProperty("after").GetProperty("targetIds").EnumerateArray()
        .Select(value => value.GetString()!.Split("::", StringSplitOptions.None)[1]).ToArray();
}
