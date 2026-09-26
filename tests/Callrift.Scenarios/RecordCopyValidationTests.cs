using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class RecordCopyValidationTests
{
    private static readonly (string Name, string Source, bool Valid)[] Fixtures =
    [
        ("private", "record State { public State() {} private State(State other) { Sink.Run(); } }", false),
        ("internal", "record State { public State() {} internal State(State other) { Sink.Run(); } }", false),
        ("protected-internal", "record State { public State() {} protected internal State(State other) { Sink.Run(); } }", false),
        ("private-protected", "record State { public State() {} private protected State(State other) { Sink.Run(); } }", false),
        ("duplicate", "record State { public State() {} protected State(State other) { Sink.Run(); } } record State;", false),
        ("missing-partial", "partial record State { public State() {} protected State(State other) { Sink.Run(); } } record State;", false),
        ("abstract-invalid", "abstract record State { protected State() {} private protected State(State other) { Sink.Run(); } } sealed record Derived : State;", false),
        ("partial-control", "partial record State { public State() {} protected State(State other) { Sink.Run(); } } partial record State;", true),
        ("abstract-control", "abstract record State { protected State() {} protected State(State other) { Sink.Run(); } } sealed record Derived : State;", true),
        ("sealed-control", "sealed record State { public State() {} internal State(State other) { Sink.Run(); } }", true)
    ];

    public static IEnumerable<object[]> Cases => Fixtures.SelectMany(fixture => new[] { new object[] { fixture.Name, false }, new object[] { fixture.Name, true } });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task InvalidRecordCopiesReportDiagnosticsAndOmitBodies(string name, bool workspace)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var source = example.Source + " static class Entry { public static void Run(State value) { _ = value with {}; } } static class Sink { public static void Run() {} public static void Other() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Run();", "Sink.Other();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("record-copy-validation", "Invalid record declarations retain diagnostics without claiming executable copy bodies.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "tree", "reach", "diff" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.Before];
            string[] target = command == "reach" ? ["--to", "Sink.Run"] : [];
            var output = await fixture.RunAsync([command, .. revisions, "--entry", "Entry.Run", .. target, "--depth", "16", "--format", "json", .. mode]);
            Assert.StartsWith("exit: 0\n", output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
            Assert.Equal(!example.Valid, diagnostics.Any(diagnostic => diagnostic.GetProperty("code").GetString() == "unresolved-record-copy"));
            if (example.Valid) Assert.Empty(diagnostics);
            var roots = document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray().ToArray();
            var calls = roots.SelectMany(Flatten).ToArray();
            Assert.Equal(example.Valid, calls.Any(node => node.GetProperty("label").GetString() == "Sink.Run"));
            if (command == "diff") Assert.Equal(example.Valid, document.RootElement.GetProperty("hasChanges").GetBoolean());
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
