using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class StaticCallbackInitializationTests
{
    [Theory]
    [InlineData("field")]
    [InlineData("method")]
    [InlineData("method-group")]
    [Trait("Layer", "Fast")]
    public Task CallbackArgumentsKeepIndependentInitializationState(string form) => VerifyCallbackArgumentsKeepIndependentInitializationState(false, form);

    [Theory]
    [InlineData("field")]
    [InlineData("method")]
    [InlineData("method-group")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceCallbackArgumentsKeepIndependentInitializationState(string form) => VerifyCallbackArgumentsKeepIndependentInitializationState(true, form);

    private static async Task VerifyCallbackArgumentsKeepIndependentInitializationState(bool workspace, string form)
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
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("static-callback-state", "Each possible callback argument retains its own first initialization; repeated accesses within one callback share state, and deferred execution cannot initialize the caller's later access.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        var tree = await fixture.QueryAsync(options);
        foreach (var command in new[] { "tree", "diff" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options) : tree;
            using var document = JsonDocument.Parse(output);
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
