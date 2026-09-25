using System.Text.Json;
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

    [Fact]
    public async Task ReferencedGeneratorBuildsBeforeAnalysis()
    {
        var before = new Dictionary<string, string>
        {
            ["App/App.csproj"] = Project.Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../Generator/Generator.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" /></ItemGroup></Project>", StringComparison.Ordinal),
            ["App/Flow.cs"] = "partial class Flow { public void Run() => Generated(); partial void Generated(); void Before() {} void After() {} }",
            ["Generator/Generator.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"4.14.0\" /></ItemGroup><Target Name=\"BuildNotice\" BeforeTargets=\"CoreCompile\" Condition=\"'$(DesignTimeBuild)' != 'true'\"><Warning Text=\"Generator compilation notice\" /></Target></Project>",
            ["Generator/FlowGenerator.cs"] = "using Microsoft.CodeAnalysis; [Generator] public sealed class FlowGenerator : IIncrementalGenerator { public void Initialize(IncrementalGeneratorInitializationContext context) => context.RegisterPostInitializationOutput(output => output.AddSource(\"Flow.g.cs\", \"partial class Flow { partial void Generated() { Before(); } }\")); }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Generator/FlowGenerator.cs"] = before["Generator/FlowGenerator.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("referenced-generator", "A changed project-reference generator changes the generated call beneath the unedited application entry point.", before, after, []));
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--project", "App/App.csproj", "--framework", "net10.0", "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("project:App/App.csproj@net10.0::Flow.Run()", root.GetProperty("after").GetProperty("symbolId").GetString());
        var generated = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal("project:App/App.csproj@net10.0::Flow.Generated()", generated.GetProperty("after").GetProperty("symbolId").GetString());
        Assert.Contains("Flow.Before", output);
        Assert.Contains("Flow.After", output);
        Assert.Contains("Flow.g.cs", output);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReferencedProjectsKeepTheirOwnFramework(bool multiTargetLibrary, bool solution)
    {
        var before = new Dictionary<string, string>
        {
            ["App.slnx"] = "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"Library/Library.csproj\" /></Solution>",
            ["App/App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup><ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>",
            ["Library/Library.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>",
            ["App/Flow.cs"] = "class Flow { public void Run(Worker worker) => worker.Run(); }",
            ["Library/Worker.cs"] = "public class Worker { public void Run() {\n#if NETSTANDARD2_0\nBefore();\n#else\nWrongFramework();\n#endif\n} void Before() {} void After() {} void WrongFramework() {} }"
        };
        var libraryFramework = multiTargetLibrary ? "netstandard2.1" : "netstandard2.0";
        if (multiTargetLibrary)
        {
            before["Library/Library.csproj"] = before["Library/Library.csproj"].Replace("<TargetFramework>netstandard2.0</TargetFramework>", "<TargetFrameworks>netstandard2.0;netstandard2.1</TargetFrameworks>", StringComparison.Ordinal);
            before["Library/Worker.cs"] = before["Library/Worker.cs"].Replace("NETSTANDARD2_0", "NETSTANDARD2_1", StringComparison.Ordinal);
        }
        var after = new Dictionary<string, string>(before)
        {
            ["Library/Worker.cs"] = before["Library/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("mixed-frameworks", "Referenced projects retain their declared framework while the root selects one target.", before, after, []));
        string[] selection = solution ? ["--solution", "App.slnx"] : ["--project", "App/App.csproj"];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. selection, "--framework", "net10.0", "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.All(document.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => Assert.Equal("workspace-warning", diagnostic.GetProperty("code").GetString()));
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("project:App/App.csproj@net10.0::Flow.Run(global::Worker)", root.GetProperty("after").GetProperty("symbolId").GetString());
        var worker = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal($"project:Library/Library.csproj@{libraryFramework}::Worker.Run()", worker.GetProperty("after").GetProperty("symbolId").GetString());
        Assert.Contains("Worker.Before", output);
        Assert.Contains("Worker.After", output);
        Assert.DoesNotContain("WrongFramework", output);
    }

    [Fact]
    public async Task EmptyFrameworkListEntriesDoNotCreateProjects()
    {
        var source = ScenarioCatalog.All.Single(s => s.Name == "guard");
        var project = Project.Replace("<TargetFramework>net10.0</TargetFramework>", "<TargetFrameworks>;net10.0;net11.0;</TargetFrameworks>", StringComparison.Ordinal);
        var scenario = source with
        {
            Before = new Dictionary<string, string>(source.Before) { ["App.csproj"] = project },
            After = new Dictionary<string, string>(source.After) { ["App.csproj"] = project }
        };
        await using var fixture = await GitFixture.CreateAsync(scenario);
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--project", "App.csproj", "--framework", "net10.0", "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Contains("if (ready)", output);
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
