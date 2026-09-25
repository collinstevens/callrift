using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DispatchCycleIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreservesRecursiveImplementationsAndTheirAncestorReferences(bool virtualCall, bool workspace)
    {
        var source = virtualCall
            ? "class Base { public virtual void Run() => Run(); }"
            : "interface IWorker { void Run(); } class First(IWorker worker) : IWorker { public void Run() => worker.Run(); }";
        var added = virtualCall
            ? " class Derived : Base { public override void Run() {} }"
            : " class Second : IWorker { public void Run() {} }";
        var project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>14.0</LangVersion></PropertyGroup></Project>";
        await using var fixture = await GitFixture.CreateAsync(new Scenario("dispatch-cycle", "Adding a target preserves the existing recursive implementation.",
            new Dictionary<string, string> { ["Flow.cs"] = source, ["App.csproj"] = project },
            new Dictionary<string, string> { ["Flow.cs"] = source + added, ["App.csproj"] = project }, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var reverse in new[] { false, true })
        {
            var output = await fixture.RunAsync(["diff", reverse ? fixture.After : fixture.Before, reverse ? fixture.Before : fixture.After,
                "--entry", virtualCall ? "Base.Run" : "First.Run", "--depth", "10", "--externals", "--format", "json", .. mode]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
            var nodes = Flatten(root).ToArray();
            var newTargets = nodes.Where(node => node.GetProperty("label").GetString() == (virtualCall ? "⇢ Derived.Run" : "⇢ Second.Run")).ToArray();
            Assert.NotEmpty(newTargets);
            Assert.All(newTargets, node => Assert.Equal(reverse ? "removed" : "added", node.GetProperty("change").GetString()));
            Assert.All(nodes.Except(newTargets), node => Assert.Equal("unchanged", node.GetProperty("change").GetString()));
            var cycle = Assert.Single(nodes, node => node.GetProperty("omission") is { ValueKind: JsonValueKind.Object } omission && omission.GetProperty("reason").GetString() == "cycle");
            var referenceId = cycle.GetProperty("omission").GetProperty("referenceId").GetString();
            if (virtualCall)
            {
                var implementation = Assert.Single(nodes, node => node.GetProperty("id").GetString() == referenceId);
                Assert.Equal("dispatchTarget", implementation.GetProperty("kind").GetString());
                Assert.Equal("⇢ Base.Run", implementation.GetProperty("label").GetString());
                Assert.Contains(implementation.GetProperty("children").EnumerateArray(), child => child.GetProperty("id").GetString() == cycle.GetProperty("id").GetString());
            }
            else
                Assert.Equal(root.GetProperty("id").GetString(), referenceId);
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
