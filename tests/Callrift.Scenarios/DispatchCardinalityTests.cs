using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DispatchCardinalityTests
{
    [Theory]
    [InlineData("single")]
    [InlineData("empty")]
    [InlineData("virtual")]
    [InlineData("replacement")]
    [InlineData("contract-root")]
    [InlineData("empty-root")]
    [InlineData("generic")]
    [InlineData("conditional")]
    [InlineData("cycle")]
    [InlineData("depth")]
    [Trait("Layer", "Fast")]
    public Task PreservesTheContractAndExistingImplementations(string name) => VerifyPreservesTheContractAndExistingImplementations(name, false);

    [Theory]
    [InlineData("single")]
    [InlineData("empty")]
    [InlineData("virtual")]
    [InlineData("replacement")]
    [InlineData("contract-root")]
    [InlineData("empty-root")]
    [InlineData("generic")]
    [InlineData("conditional")]
    [InlineData("cycle")]
    [InlineData("depth")]
    [Trait("Layer", "Integration")]
    public Task WorkspacePreservesTheContractAndExistingImplementations(string name) => VerifyPreservesTheContractAndExistingImplementations(name, true);

    private static async Task VerifyPreservesTheContractAndExistingImplementations(string name, bool workspace)
    {
        var change = Change(name);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var before = new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = change.Before };
        var after = new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = change.After };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("dispatch-" + name, "Preserve existing calls when possible implementation sets change.", before, after, []), workspace);
        var options = new DiffOptions
        {
            Entries = name.EndsWith("root", StringComparison.Ordinal) ? ["IWorker.Run"] : [],
            MaxDepth = name == "depth" ? 2 : 10,
            IncludeExternals = true
        };
        foreach (var reverse in new[] { false, true })
        {
            var output = await fixture.DiffAsync(options, reverse);
            using var result = JsonDocument.Parse(output);
            Assert.Empty(result.RootElement.GetProperty("diagnostics").EnumerateArray());
            var roots = result.RootElement.GetProperty("trees").EnumerateArray().ToArray();
            var root = Assert.Single(roots, node => node.GetProperty("label").GetString() == (name.EndsWith("root", StringComparison.Ordinal) ? change.Contract : "Entry.Run"));
            foreach (var other in roots.Where(node => node.GetProperty("id").GetString() != root.GetProperty("id").GetString()))
            {
                Assert.Contains(other.GetProperty("change").GetString(), new[] { "added", "removed" });
                var side = other.GetProperty("after").ValueKind == JsonValueKind.Object ? other.GetProperty("after") : other.GetProperty("before");
                Assert.EndsWith("..ctor()", side.GetProperty("symbolId").GetString());
            }
            var nodes = Flatten([root]).ToArray();
            var contract = Assert.Single(nodes, node => node.GetProperty("label").GetString() == change.Contract);
            Assert.Equal("unchanged", contract.GetProperty("change").GetString());
            Assert.Equal(contract.GetProperty("before").GetProperty("symbolId").GetString(), contract.GetProperty("after").GetProperty("symbolId").GetString());
            if (change.Preserved is { } preservedLabel)
            {
                var preserved = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "⇢ " + preservedLabel);
                Assert.Equal("unchanged", preserved.GetProperty("change").GetString());
                Assert.Equal(preserved.GetProperty("before").GetProperty("symbolId").GetString(), preserved.GetProperty("after").GetProperty("symbolId").GetString());
            }
            var added = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "⇢ " + change.Added);
            Assert.Equal(reverse ? "removed" : "added", added.GetProperty("change").GetString());
            if (name == "replacement")
            {
                var removed = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "⇢ First.Run");
                Assert.Equal(reverse ? "added" : "removed", removed.GetProperty("change").GetString());
            }
            if (name is "single" or "depth" or "virtual")
            {
                var kept = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Sink.Keep");
                Assert.Equal("unchanged", kept.GetProperty("change").GetString());
            }
            if (name == "cycle")
            {
                var cycle = Assert.Single(nodes, node => node.GetProperty("omission") is { ValueKind: JsonValueKind.Object } omission && omission.GetProperty("reason").GetString() == "cycle");
                Assert.Equal(root.GetProperty("id").GetString(), cycle.GetProperty("omission").GetProperty("referenceId").GetString());
            }
        }
    }

    private static IEnumerable<JsonElement> Flatten(IEnumerable<JsonElement> nodes) => nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.GetProperty("children").EnumerateArray())));

    private static (string Before, string After, string Contract, string? Preserved, string Added) Change(string name)
    {
        const string contract = "interface IWorker { void Run(); } static class Entry { public static void Run(IWorker worker) => worker.Run(); }";
        const string first = " sealed class First : IWorker { public void Run() {} }";
        const string second = " sealed class Second : IWorker { public void Run() {} }";
        const string callback = "interface IWorker { void Run(System.Action action); } sealed class First : IWorker { public void Run(System.Action action) => action(); } static class Sink { public static void Keep() {} } static class Entry { public static void Run(IWorker worker) => worker.Run(() => Sink.Keep()); }";
        const string virtualBase = "class Base { public virtual void Run() => Sink.Keep(); } static class Sink { public static void Keep() {} } static class Entry { public static void Run(Base worker) => worker.Run(); }";
        const string generic = "interface IWorker<T> { void Run(T value); } sealed class First<T> : IWorker<T> { public void Run(T value) {} } static class Entry { public static void Run(IWorker<string> worker) => worker.Run(\"value\"); }";
        const string conditional = "interface IWorker { void Run(); } sealed class First : IWorker { public void Run() {} } static class Entry { public static void Run(IWorker worker) => worker?.Run(); }";
        const string cycle = "interface IWorker { void Run(); } sealed class First : IWorker { public void Run() => Entry.Run(this); } static class Entry { public static void Run(IWorker worker) => worker.Run(); }";
        return name switch
        {
            "single" or "depth" => (callback, callback + " sealed class Second : IWorker { public void Run(System.Action action) => action(); }", "IWorker.Run", "First.Run", "Second.Run"),
            "empty" or "empty-root" => (contract, contract + first, "IWorker.Run", null, "First.Run"),
            "virtual" => (virtualBase, virtualBase + " sealed class Second : Base { public override void Run() {} }", "Base.Run", "Base.Run", "Second.Run"),
            "replacement" => (contract + first, contract + second, "IWorker.Run", null, "Second.Run"),
            "contract-root" => (contract + first, contract + first + second, "IWorker.Run", "First.Run", "Second.Run"),
            "generic" => (generic, generic + " sealed class Second<T> : IWorker<T> { public void Run(T value) {} }", "IWorker<T>.Run", "First<T>.Run", "Second<T>.Run"),
            "conditional" => (conditional, conditional + second, "IWorker.Run", "First.Run", "Second.Run"),
            "cycle" => (cycle, cycle + second, "IWorker.Run", "First.Run", "Second.Run"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }
}
