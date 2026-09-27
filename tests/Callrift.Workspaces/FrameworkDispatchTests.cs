using System.Text.Json;
using Callrift.Core;
using Callrift.MSBuild;
using Callrift.Scenarios;
using Xunit;

namespace Callrift.Workspaces;

public sealed class FrameworkDispatchTests : FrameworkDispatchFixture
{
    [Theory]
    [InlineData("netstandard2.0", false)]
    [InlineData("netstandard2.0", true)]
    public Task FrameworkInterfaceDispatchReachesReferencedImplementation(string framework, bool nested) =>
        VerifyFrameworkInterfaceDispatchAsync(framework, nested);
}

public sealed class Framework21DispatchTests : FrameworkDispatchFixture
{
    [Theory]
    [InlineData("netstandard2.1", false)]
    [InlineData("netstandard2.1", true)]
    public Task FrameworkInterfaceDispatchReachesReferencedImplementation(string framework, bool nested) =>
        VerifyFrameworkInterfaceDispatchAsync(framework, nested);
}

public abstract class FrameworkDispatchFixture
{
    protected static async Task VerifyFrameworkInterfaceDispatchAsync(string framework, bool nested)
    {
        var before = new Dictionary<string, string>
        {
            ["App/App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>",
            ["Library/Library.csproj"] = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{framework}</TargetFramework></PropertyGroup></Project>",
            ["App/Flow.cs"] = "using System; public class Flow { public int Run(IComparable<string> worker) => worker.CompareTo(\"value\"); }",
            ["Library/Worker.cs"] = "using System; public class Worker : IComparable<string> { public int CompareTo(string value) { Before(); return 0; } void Before() {} void After() {} }"
        };
        if (nested) before["Library/Worker.cs"] = before["Library/Worker.cs"].Replace("public class Worker", "public class Holder { public class Worker", StringComparison.Ordinal) + " }";
        var typeName = nested ? "Holder.Worker" : "Worker";
        var after = new Dictionary<string, string>(before)
        {
            ["Library/Worker.cs"] = before["Library/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        var scenario = new Scenario("framework-interface", "A BCL interface call reaches a referenced implementation across framework identities.", before, after, []);
        await using var fixture = framework == "netstandard2.0" && !nested ? await GitFixture.CreateAsync(scenario) : null;
        var graphs = fixture is null
            ? await ScenarioWorkspaceFixture.AnalyzeAsync(scenario, includeTests: false, options: new MSBuildOptions("App/App.csproj", NoRestore: true))
            : default;
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string output;
            if (fixture is not null)
            {
                string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
                string[] entry = command == "diff" ? [] : ["--entry", "Flow.Run"];
                string[] target = command == "reach" ? ["--to", "Worker.After"] : [];
                string[] restore = command == "diff" ? [] : ["--no-restore"];
                var rendered = await fixture.RunAsync([command, .. revisions, .. entry, .. target, "--project", "App/App.csproj", "--format", "json", .. restore]);
                Assert.True(rendered.StartsWith("exit: 0\n", StringComparison.Ordinal), rendered);
                output = rendered.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0];
            }
            else
            {
                var options = new DiffOptions { Entries = command == "diff" ? [] : ["Flow.Run"] };
                var result = command == "diff" ? CallriftService.Compare(graphs.Before, graphs.After, options)
                    : CallQueries.Query(graphs.After, new QueryRequest("unused") { Options = options, Target = command == "reach" ? "Worker.After" : null });
                output = JsonRenderer.Render(result);
            }
            using var document = JsonDocument.Parse(output);
            Assert.All(document.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => Assert.Equal("workspace-warning", diagnostic.GetProperty("code").GetString()));
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
            Assert.Contains($"project:Library/Library.csproj@{framework}::{typeName}.CompareTo(string)", output);
            Assert.Contains("Worker.After", output);
            if (command == "diff") Assert.Contains("Worker.Before", output);
        }
    }
}
