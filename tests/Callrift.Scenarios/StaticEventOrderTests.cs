using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticEventOrderTests
{
    [Theory]
    [InlineData(false, "+=")]
    [InlineData(true, "+=")]
    [InlineData(false, "-=")]
    [InlineData(true, "-=")]
    public async Task EventAssignmentEvaluatesHandlerBeforeInitialization(bool workspace, string operation)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() { State.Changed " + operation + " Factory.Create(); } } static class State { static State() { Sink.Before(); } public static event System.Action Changed { add {} remove {} } } static class Factory { public static System.Action Create() { Sink.Argument(); return () => {}; } } static class Sink { public static void Before() {} public static void After() {} public static void Argument() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("static-event-order", "Static event assignments evaluate their handler expression before triggering type initialization.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "tree", "diff" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            var output = await fixture.RunAsync([command, .. revisions, "--entry", "Entry.Run", "--depth", "20", "--format", "json", .. mode]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            var children = document.RootElement.GetProperty("trees")[0].GetProperty("children").EnumerateArray().ToArray();
            Assert.Equal("Factory.Create", children[0].GetProperty("label").GetString());
            Assert.Equal("Sink.Argument", children[0].GetProperty("children")[0].GetProperty("label").GetString());
            Assert.Equal("possible initialization of State", children[1].GetProperty("label").GetString());
            Assert.Contains("Sink.After", output);
        }
        foreach (var format in new[] { "text", "md" })
        {
            var output = await fixture.RunAsync(["tree", fixture.After, "--entry", "Entry.Run", "--depth", "20", "--format", format, .. mode]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.True(output.IndexOf("Factory.Create", StringComparison.Ordinal) < output.IndexOf("initialization of State", StringComparison.Ordinal), output);
        }
    }
}
