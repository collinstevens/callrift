using Callrift.Scenarios;
using VerifyXunit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Callrift.Workspaces;

public sealed class WorkspaceTests
{
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";

    [Theory]
    [InlineData("orders")]
    [InlineData("guard")]
    [InlineData("signature")]
    [InlineData("overloads-generics")]
    [InlineData("records-partials")]
    public async Task Parity(string name)
    {
        var original = ScenarioCatalog.All.Single(s => s.Name == name);
        var scenario = original with
        {
            Before = new Dictionary<string, string>(original.Before) { ["App.csproj"] = Project },
            After = new Dictionary<string, string>(original.After) { ["App.csproj"] = Project }
        };
        await SnapshotAsync(name, scenario, "--project", "App.csproj");
    }

    [Fact]
    public async Task PackageBinding()
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = Project.Replace("</Project>", "<ItemGroup><PackageReference Include=\"Microsoft.Extensions.DependencyInjection\" Version=\"10.0.1\" /></ItemGroup></Project>", StringComparison.Ordinal),
            ["Flow.cs"] = "using Microsoft.Extensions.DependencyInjection; interface IWorker { void Run(); } class Worker : IWorker { public void Run() { Before(); } void Before() {} void After() {} } class Flow { public void Start() { var services = new ServiceCollection().AddSingleton<IWorker, Worker>().BuildServiceProvider(); var worker = services.GetRequiredService<IWorker>(); worker.Run(); } }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await SnapshotAsync("package-binding", new Scenario("package", "Package generic return types bind the inferred receiver to the source interface.", before, after, []), "--project", "App.csproj");
    }

    [Fact]
    public async Task Defines()
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = Project.Replace("</PropertyGroup>", "<DefineConstants>FEATURE</DefineConstants></PropertyGroup>", StringComparison.Ordinal),
            ["Flow.cs"] = "class Flow { public void Run() {\n#if FEATURE\nBefore();\n#else\nHidden();\n#endif\n} void Before() {} void After() {} void Hidden() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await SnapshotAsync("defines", new Scenario("defines", "The actual project define includes the changed call and excludes the alternative.", before, after, []), "--project", "App.csproj");
    }

    [Fact]
    public async Task Projects()
    {
        var before = new Dictionary<string, string>
        {
            ["App.slnx"] = "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"A/A.csproj\" /><Project Path=\"B/B.csproj\" /></Solution>",
            ["A/A.csproj"] = Project,
            ["B/B.csproj"] = Project,
            ["App/App.csproj"] = Project.Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../A/A.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal),
            ["A/Worker.cs"] = "namespace Shared; public class Worker { public void Run() { Before(); } void Before() {} void After() {} }",
            ["B/Worker.cs"] = "namespace Shared; public class Worker { public void Run() { Other(); } void Other() {} }",
            ["App/Flow.cs"] = "class Flow { public void Start(Shared.Worker worker) => worker.Run(); }"
        };
        var after = new Dictionary<string, string>(before) { ["A/Worker.cs"] = before["A/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        await SnapshotAsync("projects", new Scenario("projects", "Identical type names in separate projects stay distinct and project-reference calls expand.", before, after, []), "--solution", "App.slnx");
    }

    [Fact]
    public async Task Generator()
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = Project,
            ["Flow.cs"] = "using System.Text.RegularExpressions; partial class Flow { public bool Match(string value) => Pattern().IsMatch(value); [GeneratedRegex(\"before\")] private static partial Regex Pattern(); }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("\"before\"", "\"after\"", StringComparison.Ordinal) };
        await SnapshotAsync("generator", new Scenario("generator", "SDK regex generator bodies participate in change detection; static fields and property accessors do not link the runner back to Flow.Match.", before, after, []), "--project", "App.csproj");
    }

    private static async Task SnapshotAsync(string name, Scenario scenario, params string[] selection)
    {
        await using var fixture = await GitFixture.CreateAsync(scenario);
        var outputs = new List<string> { scenario.Description };
        foreach (var format in new[] { "text", "md", "json" })
            outputs.Add("format: " + format + "\n" + await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", format,
                .. selection, .. format == "text" ? Array.Empty<string>() : ["--no-restore"]]));
        await Verifier.Verify(string.Join("\n", outputs).Replace(fixture.Before, "<before>", StringComparison.Ordinal).Replace(fixture.After, "<after>", StringComparison.Ordinal))
            .UseDirectory("Snapshots").UseFileName(name).DisableDiff();
    }

    [Fact]
    public async Task RequiresRestoredCacheAndExplicitFramework()
    {
        var source = ScenarioCatalog.All.Single(s => s.Name == "guard");
        var project = Project.Replace("<TargetFramework>net10.0</TargetFramework>", "<TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>", StringComparison.Ordinal);
        var scenario = source with
        {
            Before = new Dictionary<string, string>(source.Before) { ["App.csproj"] = project },
            After = new Dictionary<string, string>(source.After) { ["App.csproj"] = project }
        };
        await using var fixture = await GitFixture.CreateAsync(scenario);
        var missing = await fixture.RunAsync("tree", fixture.After, "--entry", "Flow.Run", "--project", "App.csproj", "--no-restore");
        Assert.Contains("exit: 2", missing);
        Assert.Contains("No restored workspace cache exists", missing);
        var selected = await fixture.RunAsync("tree", fixture.After, "--entry", "Flow.Run", "--project", "App.csproj", "--framework", "net10.0");
        Assert.Contains("exit: 0", selected);
        Assert.Contains("if (ready)", selected);
        var conflicting = await fixture.RunAsync("--mode", "source", "--project", "App.csproj");
        Assert.Contains("exit: 2", conflicting);
    }
}
