using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticEventOrderTests
{
    [Theory]
    [InlineData("+=")]
    [InlineData("-=")]
    [Trait("Layer", "Fast")]
    public Task EventAssignmentEvaluatesHandlerBeforeInitialization(string operation) => VerifyEventAssignmentEvaluatesHandlerBeforeInitialization(false, operation);

    [Theory]
    [InlineData("+=")]
    [InlineData("-=")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceEventAssignmentEvaluatesHandlerBeforeInitialization(string operation) => VerifyEventAssignmentEvaluatesHandlerBeforeInitialization(true, operation);

    private static async Task VerifyEventAssignmentEvaluatesHandlerBeforeInitialization(bool workspace, string operation)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() { State.Changed " + operation + " Factory.Create(); } } static class State { static State() { Sink.Before(); } public static event System.Action Changed { add {} remove {} } } static class Factory { public static System.Action Create() { Sink.Argument(); return () => {}; } } static class Sink { public static void Before() {} public static void After() {} public static void Argument() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("static-event-order", "Static event assignments evaluate their handler expression before triggering type initialization.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        var trees = await fixture.QueryFormatsAsync(options);
        foreach (var command in new[] { "tree", "diff" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options) : trees["json"];
            using var document = JsonDocument.Parse(output);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            var children = document.RootElement.GetProperty("trees")[0].GetProperty("children").EnumerateArray().ToArray();
            Assert.Equal("Factory.Create", children[0].GetProperty("label").GetString());
            Assert.Equal("Sink.Argument", children[0].GetProperty("children")[0].GetProperty("label").GetString());
            Assert.Equal("possible initialization of State", children[1].GetProperty("label").GetString());
            Assert.Contains("Sink.After", output);
        }
        foreach (var format in new[] { "text", "md" })
        {
            var output = trees[format];
            Assert.True(output.IndexOf("Factory.Create", StringComparison.Ordinal) < output.IndexOf("initialization of State", StringComparison.Ordinal), output);
        }
    }
}
