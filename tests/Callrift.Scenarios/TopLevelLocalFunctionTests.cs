using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class TopLevelLocalFunctionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public async Task SeparateProgramsKeepTheirLocalFunctions(bool nested)
    {
        var scenario = CreateScenario(nested);
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, false);
        using var diff = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { Context = -1 }));
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("First/Program.cs", root.GetProperty("after").GetProperty("definition").GetProperty("path").GetString());
        Assert.Contains(Descendants(root.GetProperty("children")), node => node.GetProperty("label").GetString() == "FirstSink.After"
            && node.GetProperty("change").GetString() == "added");
        var options = new DiffOptions { Entries = ["Second/Program.cs::<top-level>"] };
        using var tree = JsonDocument.Parse(await fixture.QueryAsync(options));
        VerifySecondProgram(tree.RootElement);
        using var reach = JsonDocument.Parse(await fixture.QueryAsync(options, target: "FirstSink.After"));
        Assert.Empty(reach.RootElement.GetProperty("paths").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliKeepsTopLevelLocalFunctionsInTheirProjects(bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario(true));
        var restored = false;
        using var diff = await Run("diff", fixture.Before, fixture.After);
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("First/Program.cs", root.GetProperty("after").GetProperty("definition").GetProperty("path").GetString());
        Assert.Contains(Descendants(root.GetProperty("children")), node => node.GetProperty("label").GetString() == "FirstSink.After"
            && node.GetProperty("change").GetString() == "added");
        using var tree = await Run("tree", fixture.After, "--entry", "Second/Program.cs::<top-level>");
        VerifySecondProgram(tree.RootElement);
        using var reach = await Run("reach", fixture.After, "--entry", "Second/Program.cs::<top-level>", "--to", "FirstSink.After");
        Assert.Empty(reach.RootElement.GetProperty("paths").EnumerateArray());

        async Task<JsonDocument> Run(params string[] command)
        {
            var arguments = command.Concat(["--format", "json", "--context", "all"]).ToList();
            if (workspace)
            {
                arguments.AddRange(["--project", "App.csproj"]);
                if (restored) arguments.Add("--no-restore");
            }
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            restored = true;
            return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        }
    }

    private static Scenario CreateScenario(bool nested)
    {
        var body = nested ? "Run(); void Run() { Work(); void Work() => SINK.Before(); }" : "Run(); void Run() => SINK.Before();";
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><ProjectReference Include=\"First/First.csproj\" /><ProjectReference Include=\"Second/Second.csproj\" /></ItemGroup></Project>",
            ["First/First.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>",
            ["Second/Second.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>",
            ["First/Program.cs"] = body.Replace("SINK", "FirstSink", StringComparison.Ordinal),
            ["Second/Program.cs"] = body.Replace("SINK", "SecondSink", StringComparison.Ordinal),
            ["First/Sink.cs"] = "static class FirstSink { public static void Before() {} public static void After() {} }",
            ["Second/Sink.cs"] = "static class SecondSink { public static void Before() {} }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["First/Program.cs"] = before["First/Program.cs"].Replace("FirstSink.Before", "FirstSink.After", StringComparison.Ordinal)
        };
        return new Scenario("top-level-local-functions", "Top-level local functions retain their declaring file and their own callers.", before, after, []);
    }

    private static void VerifySecondProgram(JsonElement result)
    {
        Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
        var nodes = Descendants(result.GetProperty("trees")).ToArray();
        Assert.Contains(nodes, node => node.GetProperty("label").GetString() == "SecondSink.Before");
        Assert.DoesNotContain(nodes, node => node.GetProperty("label").GetString()!.StartsWith("FirstSink.", StringComparison.Ordinal));
        foreach (var node in nodes.Where(node => node.GetProperty("label").GetString()!.Contains("<Main>$", StringComparison.Ordinal)))
            Assert.Equal("Second/Program.cs", node.GetProperty("after").GetProperty("definition").GetProperty("path").GetString());
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement nodes)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            yield return node;
            foreach (var child in Descendants(node.GetProperty("children"))) yield return child;
        }
    }
}
