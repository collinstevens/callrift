using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstructorDiagnosticTests
{
    [Fact]
    [Trait("Layer", "Fast")]
    public Task DuplicateConstructorBodiesRemainUnexpanded() => VerifyDuplicateConstructorBodiesRemainUnexpanded(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceDuplicateConstructorBodiesRemainUnexpanded() => VerifyDuplicateConstructorBodiesRemainUnexpanded(true);

    private static async Task VerifyDuplicateConstructorBodiesRemainUnexpanded(bool workspace)
    {
        const string source = "class Derived { public Derived() {} public Derived() {} }";
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>14.0</LangVersion></PropertyGroup></Project>";
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("duplicate-constructor", "Ambiguous constructor bodies remain diagnosed and unexpanded.",
            new Dictionary<string, string> { ["Flow.cs"] = source, ["App.csproj"] = project },
            new Dictionary<string, string> { ["Flow.cs"] = source + "\n", ["App.csproj"] = project }, []), workspace);
        var output = await fixture.QueryAsync(new DiffOptions { Entries = ["new Derived"], IncludeExternals = true }, before: true);
        using var document = JsonDocument.Parse(output);
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "duplicate-member");
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("new Derived", root.GetProperty("label").GetString());
        Assert.Empty(root.GetProperty("children").EnumerateArray());
    }
}
