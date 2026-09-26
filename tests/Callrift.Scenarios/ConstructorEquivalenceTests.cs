using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstructorEquivalenceTests
{
    public static IEnumerable<object[]> Cases => new[] { "explicit-base", "primary-base", "class-empty", "struct-empty", "object-base", "metadata-base", "metadata-primary" }
        .SelectMany(name => new[] { new object[] { name, false }, new object[] { name, true } });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EquivalentConstructorSyntaxPreservesTheCallGraph(string name, bool workspace)
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
        await using var fixture = await GitFixture.CreateAsync(new Scenario("constructor-equivalence", "Explicit spelling of implicit constructor behavior preserves calls.",
            new Dictionary<string, string> { ["Flow.cs"] = before + entry, ["App.csproj"] = project },
            new Dictionary<string, string> { ["Flow.cs"] = after + entry, ["App.csproj"] = project }, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var focused in new[] { false, true })
            foreach (var reverse in new[] { false, true })
            {
                string[] selection = focused ? ["--entry", "Entry.Run"] : [];
                var output = await fixture.RunAsync(["diff", reverse ? fixture.After : fixture.Before, reverse ? fixture.Before : fixture.After,
                    "--format", "json", "--depth", "14", "--externals", .. selection, .. mode]);
                Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
                using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
                Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.False(document.RootElement.GetProperty("hasChanges").GetBoolean());
                Assert.Empty(document.RootElement.GetProperty("trees").EnumerateArray());
            }
    }
}
