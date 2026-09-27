using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstructorEquivalenceTests
{
    public static IEnumerable<object[]> Cases => new[] { "explicit-base", "primary-base", "class-empty", "struct-empty", "object-base", "metadata-base", "metadata-primary" }
        .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public async Task EquivalentConstructorSyntaxPreservesTheCallGraph(string name)
    {
        var (before, after) = await SourceFixture.AnalyzeAsync(CreateScenario(name));
        foreach (var focused in new[] { false, true })
            foreach (var reverse in new[] { false, true })
            {
                var options = new DiffOptions { MaxDepth = 14, IncludeExternals = true, Entries = focused ? ["Entry.Run"] : [] };
                var result = CallriftService.Compare(reverse ? after : before, reverse ? before : after, options);
                Assert.Empty(result.Diagnostics);
                Assert.False(result.HasChanges);
                Assert.Empty(result.Trees);
            }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public async Task WorkspaceConstructorSyntaxPreservesTheCallGraph(string name)
    {
        await using var fixture = name == "explicit-base"
            ? await AnalysisFixture.CreateWorkspaceCliAsync(CreateScenario(name))
            : await AnalysisFixture.CreateAsync(CreateScenario(name), workspace: true);
        foreach (var focused in new[] { false, true })
            foreach (var reverse in new[] { false, true })
            {
                var options = new DiffOptions { MaxDepth = 14, IncludeExternals = true, Entries = focused ? ["Entry.Run"] : [] };
                using var document = JsonDocument.Parse(await fixture.DiffAsync(options, reverse));
                Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.False(document.RootElement.GetProperty("hasChanges").GetBoolean());
                Assert.Empty(document.RootElement.GetProperty("trees").EnumerateArray());
            }
    }

    private static Scenario CreateScenario(string name)
    {
        var (before, after) = name switch
        {
            "object-base" => ("class Derived { public Derived() {} }", "class Derived { public Derived() : base() {} }"),
            "metadata-base" => ("class Derived : System.Exception { public Derived() {} }", "class Derived : System.Exception { public Derived() : base() {} }"),
            "metadata-primary" => ("class Derived() : System.Exception {}", "class Derived() : System.Exception() {}"),
            "explicit-base" => ("class Base {} class Derived : Base { public Derived() {} }", "class Base {} class Derived : Base { public Derived() : base() {} }"),
            "primary-base" => ("class Base {} class Derived() : Base {}", "class Base {} class Derived() : Base() {}"),
            "class-empty" => ("class Derived {}", "class Derived { public Derived() {} }"),
            "struct-empty" => ("struct Derived {}", "struct Derived { public Derived() {} }"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        const string entry = " static class Entry { public static void Run() { _ = new Derived(); } }";
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>14.0</LangVersion></PropertyGroup></Project>";
        return new Scenario("constructor-equivalence", "Explicit spelling of implicit constructor behavior preserves calls.",
            new Dictionary<string, string> { ["Flow.cs"] = before + entry, ["App.csproj"] = project },
            new Dictionary<string, string> { ["Flow.cs"] = after + entry, ["App.csproj"] = project }, []);
    }
}
