using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class CompilerDiagnosticTests
{
    [Theory]
    [InlineData("System.Console.WriteLine(1)", "CS1002")]
    [InlineData("int value = ;", "CS1525")]
    [InlineData("int first = ; int second = ;", "CS1525")]
    [Trait("Layer", "Fast")]
    public Task SyntaxErrorsHaveExactSpans(string statement, string code) => VerifySyntaxErrorAsync(statement, code, false);

    [Theory]
    [InlineData("System.Console.WriteLine(1)", "CS1002")]
    [InlineData("int value = ;", "CS1525")]
    [InlineData("int first = ; int second = ;", "CS1525")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceSyntaxErrorsAreReportedOnce(string statement, string code) => VerifySyntaxErrorAsync(statement, code, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliReportsSyntaxErrorsOnceAcrossCommandsAndFormats(bool workspace)
    {
        const string source = "static class Flow { public static void Entry() { Sink.Before(); } } static class Sink { public static void Before() {} public static void After() {} }";
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var scenario = new Scenario("cli-syntax-error", "Syntax errors remain visible once in every command and format.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After()", StringComparison.Ordinal) }, []);
        await using var fixture = await GitFixture.CreateAsync(scenario);
        var restored = false;
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            foreach (var format in new[] { "text", "md", "json" })
            {
                var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
                if (command == "diff") arguments.Add(fixture.After);
                arguments.AddRange(["--entry", "Flow.Entry", "--format", format, "--strict"]);
                if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
                if (workspace)
                {
                    arguments.AddRange(["--project", "App.csproj"]);
                    if (restored) arguments.Add("--no-restore");
                }
                var output = await fixture.RunAsync(arguments.ToArray());
                Assert.StartsWith("exit: 2\n", output);
                var stderr = output.Split("stderr:\n", StringSplitOptions.None)[1];
                Assert.Single(stderr.Split('\n'), line => line.Contains("CS1002:", StringComparison.Ordinal));
                if (format == "json")
                {
                    using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
                    var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("code").GetString() == "CS1002");
                    var location = diagnostic.GetProperty("location");
                    Assert.Equal(63, location.GetProperty("startColumn").GetInt32());
                    Assert.Equal(64, location.GetProperty("endColumn").GetInt32());
                }
                restored = workspace;
            }
        }
    }

    private static async Task VerifySyntaxErrorAsync(string statement, string code, bool workspace)
    {
        var source = "class Flow\n{\n    static void Entry()\n    {\n        " + statement + "\n    }\n}";
        var syntax = CSharpSyntaxTree.ParseText(source, path: "Flow.cs");
        var errors = syntax.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Equal(code, error.Id));
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var files = new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("syntax-error-spans", "Compiler errors retain their exact source span without duplicates.", files, files, []), workspace);
        using var document = JsonDocument.Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Entry"] }));
        var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().Where(item => item.GetProperty("code").GetString() == code)
            .OrderBy(item => item.GetProperty("location").GetProperty("startColumn").GetInt32()).ToArray();
        Assert.Equal(errors.Length, diagnostics.Length);
        for (var index = 0; index < errors.Length; index++)
        {
            var error = errors[index];
            var diagnostic = diagnostics[index];
            var span = error.Location.GetLineSpan();
            Assert.Equal(error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), diagnostic.GetProperty("message").GetString());
            var location = diagnostic.GetProperty("location");
            Assert.Equal("Flow.cs", location.GetProperty("path").GetString());
            Assert.Equal(span.StartLinePosition.Line + 1, location.GetProperty("startLine").GetInt32());
            Assert.Equal(span.StartLinePosition.Character + 1, location.GetProperty("startColumn").GetInt32());
            Assert.Equal(span.EndLinePosition.Line + 1, location.GetProperty("endLine").GetInt32());
            Assert.Equal(span.EndLinePosition.Character + 1, location.GetProperty("endColumn").GetInt32());
        }
    }
}
