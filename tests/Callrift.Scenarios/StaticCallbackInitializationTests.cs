using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticCallbackInitializationTests
{
    [Theory]
    [InlineData(false, "field")]
    [InlineData(true, "field")]
    [InlineData(false, "method")]
    [InlineData(true, "method")]
    [InlineData(false, "method-group")]
    [InlineData(true, "method-group")]
    public async Task CallbackArgumentsKeepIndependentInitializationState(bool workspace, string form)
    {
        var expression = form == "field" ? "State.Value" : "State.Read()";
        var first = form == "method-group" ? "State.Read" : "() => { _ = " + expression + "; return " + expression + "; }";
        var second = form == "method-group" ? "State.Read" : "() => " + expression;
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() { Hold.Accept(" + first + ", " + second + "); _ = State.Value; } } static class Hold { public static void Accept(System.Func<int> first, System.Func<int> second) { _ = second(); } } static class State { public static int Value; static State() { Sink.Before(); } public static int Read() => Value; } static class Sink { public static void Before() {} public static void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("static-callback-state", "Each possible callback argument retains its own first initialization; repeated accesses within one callback share state, and deferred execution cannot initialize the caller's later access.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "tree", "diff" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            var output = await fixture.RunAsync([command, .. revisions, "--entry", "Entry.Run", "--depth", "20", "--format", "json", .. mode]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
            Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            var root = document.RootElement.GetProperty("trees")[0];
            var registration = Assert.Single(root.GetProperty("children").EnumerateArray(), node => node.GetProperty("label").GetString() == "Hold.Accept");
            var callbacks = registration.GetProperty("children").EnumerateArray().Where(node => node.GetProperty("label").GetString() == "possible initialization of State").ToArray();
            Assert.Equal(2, callbacks.Length);
            Assert.All(callbacks, callback => Assert.Equal("callback", callback.GetProperty("after").GetProperty("relation").GetString()));
            var initializers = Flatten(root).Where(node => node.GetProperty("label").GetString() == "initialization of State").ToArray();
            Assert.Equal(3, initializers.Length);
            Assert.All(initializers, initializer => Assert.Contains("Sink.After", initializer.ToString()));
            var direct = Assert.Single(root.GetProperty("children").EnumerateArray(), node => node.GetProperty("label").GetString() == "possible initialization of State");
            Assert.Equal("branch", direct.GetProperty("after").GetProperty("relation").GetString());
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
