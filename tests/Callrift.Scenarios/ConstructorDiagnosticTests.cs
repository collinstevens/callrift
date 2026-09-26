using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstructorDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateConstructorBodiesRemainUnexpanded(bool workspace)
    {
        const string source = "class Derived { public Derived() {} public Derived() {} }";
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>14.0</LangVersion></PropertyGroup></Project>";
        await using var fixture = await GitFixture.CreateAsync(new Scenario("duplicate-constructor", "Ambiguous constructor bodies remain diagnosed and unexpanded.",
            new Dictionary<string, string> { ["Flow.cs"] = source, ["App.csproj"] = project },
            new Dictionary<string, string> { ["Flow.cs"] = source + "\n", ["App.csproj"] = project }, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["tree", fixture.Before, "--entry", "new Derived", "--format", "json", "--externals", .. mode]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "duplicate-member");
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("new Derived", root.GetProperty("label").GetString());
        Assert.Empty(root.GetProperty("children").EnumerateArray());
    }
}
