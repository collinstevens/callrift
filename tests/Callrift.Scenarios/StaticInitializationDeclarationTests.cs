using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticInitializationDeclarationTests
{
    private static readonly (string Name, string Source, string? Diagnostic, bool Initializer, bool Sink)[] Fixtures =
    [
        ("extern-constructor", "class State { static extern State(); public static void Touch() {} }", "unavailable-static-initializer", true, false),
        ("duplicate-constructor", "class State { static State() { Sink.Before(); } static State() { Sink.Before(); } public static void Touch() {} }", "duplicate-member", true, false),
        ("duplicate-class", "class State { static int value = Sink.Before(); public static void Touch() { _ = value; } } class State {}", "unresolved-static-initializer", true, false),
        ("missing-partial", "partial class State { static int value = Sink.Before(); public static void Touch() { _ = value; } } class State {}", "unresolved-static-initializer", true, false),
        ("valid-partial", "partial class State { static int value = Sink.Before(); public static void Touch() { _ = value; } } partial class State {}", null, true, true),
        ("invalid-static-constant", "class State { static const int value = Sink.Before(); public static void Touch() { _ = value; } }", null, false, false),
        ("empty-static-constructor", "class State { static State() {} public static void Touch() {} }", null, true, false)
    ];

    public static IEnumerable<object[]> Cases => Fixtures.Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task KeepsUnavailableInitializationVisible(string name) => VerifyKeepsUnavailableInitializationVisible(name);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public async Task WorkspaceKeepsUnavailableInitializationVisible(string name)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var source = example.Source + " static class Sink { public static int Before() => 1; public static int After() => 2; } static class Entry { public static void Run() { State.Touch(); } }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Flow.cs"] = source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal).Replace("Before() => 1", "Before() => 3", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("static-declaration-" + name, "Unavailable initializer bodies remain visible without inventing calls through invalid declarations.", before, after, []));
        string[] mode = ["--project", "App.csproj"];
        using var tree = ParseCli(await fixture.RunAsync(["tree", fixture.Before, "--entry", "Entry.Run", "--format", "json", "--depth", "15", .. mode]));
        var labels = Flatten(tree.RootElement.GetProperty("trees")).Select(node => node.GetProperty("label").GetString()).ToArray();
        Assert.Equal(example.Initializer, labels.Contains("initialization of State"));
        Assert.Equal(example.Sink, labels.Contains("Sink.Before"));
        if (example.Diagnostic is not null)
            Assert.Contains(tree.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == example.Diagnostic);
        else if (name != "invalid-static-constant") Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        using var reach = ParseCli(await fixture.RunAsync(["reach", fixture.Before, "--entry", "Entry.Run", "--to", "Sink.Before", "--format", "json", "--depth", "15", "--no-restore", .. mode]));
        Assert.Equal(example.Sink, reach.RootElement.GetProperty("paths").GetArrayLength() > 0);
        foreach (var format in new[] { "text", "md" })
        {
            var rendered = await fixture.RunAsync(["tree", fixture.Before, "--entry", "Entry.Run", "--format", format, "--depth", "15", "--no-restore", .. mode]);
            Assert.StartsWith("exit: 0\n", rendered);
            Assert.Equal(example.Initializer, rendered.Contains("initialization of State", StringComparison.Ordinal));
            if (example.Diagnostic is not null) Assert.Contains(example.Diagnostic, rendered);
        }
    }

    private static async Task VerifyKeepsUnavailableInitializationVisible(string name)
    {
        var example = Fixtures.Single(fixture => fixture.Name == name);
        var source = example.Source + " static class Sink { public static int Before() => 1; public static int After() => 2; } static class Entry { public static void Run() { State.Touch(); } }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Flow.cs"] = source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal).Replace("Before() => 1", "Before() => 3", StringComparison.Ordinal)
        };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("static-declaration-" + name, "Unavailable initializer bodies remain visible without inventing calls through invalid declarations.", before, after, []), workspace: false);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 15 };
        var outputs = await fixture.QueryFormatsAsync(options, before: true);
        using var tree = Parse(outputs["json"]);
        var labels = Flatten(tree.RootElement.GetProperty("trees")).Select(node => node.GetProperty("label").GetString()).ToArray();
        Assert.Equal(example.Initializer, labels.Contains("initialization of State"));
        Assert.Equal(example.Sink, labels.Contains("Sink.Before"));
        if (example.Diagnostic is not null)
            Assert.Contains(tree.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == example.Diagnostic);
        else if (name != "invalid-static-constant") Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        using var reach = Parse(await fixture.QueryAsync(options, before: true, target: "Sink.Before"));
        Assert.Equal(example.Sink, reach.RootElement.GetProperty("paths").GetArrayLength() > 0);
        foreach (var format in new[] { "text", "md" })
        {
            var rendered = outputs[format];
            Assert.Equal(example.Initializer, rendered.Contains("initialization of State", StringComparison.Ordinal));
        }
    }

    private static JsonDocument ParseCli(string output)
    {
        Assert.StartsWith("exit: 0\n", output);
        return Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }

    private static JsonDocument Parse(string output)
    {
        return JsonDocument.Parse(output);
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes) => nodes.EnumerateArray().SelectMany(node => new[] { node }.Concat(Flatten(node.GetProperty("children"))));
}
