using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverGuardTests
{
    [Theory]
    [InlineData("positive", true)]
    [InlineData("negative-else", true)]
    [InlineData("early-return", true)]
    [InlineData("short-circuit", true)]
    [InlineData("conditional", true)]
    [InlineData("pattern-and", true)]
    [InlineData("assignment", false)]
    [InlineData("join", false)]
    [InlineData("ref-alias", false)]
    [InlineData("ref-call", false)]
    [InlineData("loop-write", false)]
    [InlineData("deferred", false)]
    [InlineData("captured-write", false)]
    [InlineData("callback-guard", true)]
    [InlineData("deconstruction", false)]
    [InlineData("goto", false)]
    [Trait("Layer", "Fast")]
    public Task GuardConstrainsDispatch(string name, bool narrowed) => VerifyAsync(name, narrowed, false);

    [Theory]
    [InlineData("positive", true)]
    [InlineData("negative-else", true)]
    [InlineData("early-return", true)]
    [InlineData("short-circuit", true)]
    [InlineData("conditional", true)]
    [InlineData("pattern-and", true)]
    [InlineData("assignment", false)]
    [InlineData("join", false)]
    [InlineData("ref-alias", false)]
    [InlineData("ref-call", false)]
    [InlineData("loop-write", false)]
    [InlineData("deferred", false)]
    [InlineData("captured-write", false)]
    [InlineData("callback-guard", true)]
    [InlineData("deconstruction", false)]
    [InlineData("goto", false)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceGuardConstrainsDispatch(string name, bool narrowed) => VerifyAsync(name, narrowed, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliBoxedCharacterExcludesReferenceOverrides(bool workspace)
    {
        const string source = """
            static class Flow { public static void Entry(object value) { if (value is char) value.ToString(); } }
            class Text { public override string ToString() { Sink.Before(); return ""; } }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var scenario = new Scenario("boxed-character-guard", "A boxed character cannot dispatch to source reference-type overrides.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) }, []);
        await using var fixture = await GitFixture.CreateAsync(scenario);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            foreach (var format in new[] { "text", "md", "json" })
            {
                var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
                if (command == "diff") arguments.Add(fixture.After);
                else arguments.AddRange(["--entry", "Flow.Entry"]);
                if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
                arguments.AddRange(["--format", format, "--externals"]);
                if (workspace) arguments.AddRange(["--project", "App.csproj"]);
                var output = await fixture.RunAsync(arguments.ToArray());
                Assert.StartsWith("exit: 0\n", output);
                if (command == "diff") Assert.DoesNotContain("Flow.Entry", output);
                else Assert.DoesNotContain("Text.ToString", output);
                if (command == "tree") Assert.Contains("value.ToString", output);
            }
        }
    }

    private static async Task VerifyAsync(string name, bool narrowed, bool workspace)
    {
        var body = name switch
        {
            "positive" => "if (value is Left) value.Run();",
            "negative-else" => "if (value is not Left) {} else value.Run();",
            "early-return" => "if (!(value is Left)) return; value.Run();",
            "short-circuit" => "_ = value is Left && value.Run();",
            "conditional" => "_ = value is Left ? value.Run() : false;",
            "pattern-and" => "if (value is Left and not null) value.Run();",
            "assignment" => "if (value is Left) { value = new Right(); value.Run(); }",
            "join" => "if (value is Left) {} value.Run();",
            "ref-alias" => "ref Worker alias = ref value; if (value is Left) { alias = new Right(); value.Run(); }",
            "ref-call" => "if (value is Left) { Replace(ref value); value.Run(); }",
            "loop-write" => "if (value is Left) { for (int i = 0; i < 2; i++) { value.Run(); value = new Right(); } }",
            "deferred" => "if (value is Left) { Save(() => value.Run()); value = new Right(); }",
            "captured-write" => "System.Action mutate = () => value = new Right(); if (value is Left) { mutate(); value.Run(); }",
            "callback-guard" => "Save(() => { if (value is Left) value.Run(); });",
            "deconstruction" => "if (value is Left) { (value, _) = (new Right(), 1); value.Run(); }",
            "goto" => "if (value is Left) { again: value.Run(); value = new Right(); goto again; }",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var source = $$"""
            abstract class Worker { public abstract bool Run(); }
            sealed class Left : Worker { public override bool Run() { Sink.Left(); return true; } }
            sealed class Right : Worker { public override bool Run() { Sink.Before(); return true; } }
            static class Flow {
                public static void Entry(Worker value) { {{body}} }
                static void Replace(ref Worker value) => value = new Right();
                static void Save(System.Action action) {}
            }
            static class Sink { public static void Left() {} public static void Before() {} public static void After() {} }
            """;
        var compilation = new SourceOnlyAnalysisProvider().CreateCompilation([CSharpSyntaxTree.ParseText(source)]);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var scenario = new Scenario("receiver-guard-" + name, "Guard facts narrow receiver dispatch only while the value remains stable.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) }, []);
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Flow.Entry"], MaxDepth = 8 };
        foreach (var command in new[] { "tree", "reach", "diff" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(new DiffOptions { MaxDepth = 8 })
                : await fixture.QueryAsync(options, target: command == "reach" ? "Sink.After" : null);
            using var json = JsonDocument.Parse(output);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command == "tree")
            {
                Assert.Contains("Left.Run", output);
                Assert.Equal(!narrowed, output.Contains("Right.Run", StringComparison.Ordinal));
            }
            else if (command == "reach")
                Assert.Equal(!narrowed, json.RootElement.GetProperty("paths").GetArrayLength() > 0);
            else
                Assert.Equal(!narrowed, json.RootElement.GetProperty("trees").EnumerateArray().Any(node => node.GetProperty("label").GetString() == "Flow.Entry"));
        }
    }
}
