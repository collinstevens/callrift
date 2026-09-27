using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class DeclarationSignatureCliTests
{
    [Theory]
    [InlineData(false, "default", "int value = 1", "int value = 2", "Worker.Run([int value = 1])", "Worker.Run([int value = 2])")]
    [InlineData(true, "default", "int value = 1", "int value = 2", "Worker.Run([int value = 1])", "Worker.Run([int value = 2])")]
    [InlineData(false, "name", "int value = 1", "int renamed = 1", "Worker.Run([int value = 1])", "Worker.Run([int renamed = 1])")]
    [InlineData(true, "name", "int value = 1", "int renamed = 1", "Worker.Run([int value = 1])", "Worker.Run([int renamed = 1])")]
    public async Task UneditedCallerExposesDeclarationChanges(bool workspace, string name, string beforeParameter, string afterParameter,
        string beforeSignature, string afterSignature)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() => Worker.Run(); } static class Worker { public static void Run(" + beforeParameter
                + ") => Sink.Keep(); } static class Sink { public static void Keep() {} }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Flow.cs"] = before["Flow.cs"].Replace(beforeParameter, afterParameter, StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("declaration-" + name,
            "A declaration-only edit affects its unchanged caller and preserves the call subtree.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        string[] reusedMode = workspace ? [.. mode, "--no-restore"] : [];
        var first = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", "json", .. mode]);
        using var diff = Parse(first);
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Entry.Run", root.GetProperty("label").GetString());
        var call = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal("Worker.Run", call.GetProperty("label").GetString());
        Assert.Equal("modified", call.GetProperty("change").GetString());
        Assert.Equal("signature changed", call.GetProperty("detail").GetString());
        AssertSignature(call.GetProperty("before"), beforeSignature);
        AssertSignature(call.GetProperty("after"), afterSignature);
        Assert.Equal(call.GetProperty("before").GetProperty("symbolId").GetString(), call.GetProperty("after").GetProperty("symbolId").GetString());
        var kept = Assert.Single(call.GetProperty("children").EnumerateArray());
        Assert.Equal("Sink.Keep", kept.GetProperty("label").GetString());
        Assert.Equal("unchanged", kept.GetProperty("change").GetString());
        Assert.Equal(first, await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", "json", .. reusedMode]));

        foreach (var (revision, signature) in new[] { (fixture.Before, beforeSignature), (fixture.After, afterSignature) })
            foreach (var command in new[] { "tree", "reach" })
            {
                string[] target = command == "reach" ? ["--to", "Sink.Keep"] : [];
                using var query = Parse(await fixture.RunAsync([command, revision, "--entry", "Entry.Run", "--format", "json", .. target, .. reusedMode]));
                Assert.Empty(query.RootElement.GetProperty("diagnostics").EnumerateArray());
                var tree = Assert.Single(query.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
                AssertSignature(Assert.Single(tree.GetProperty("children").EnumerateArray()).GetProperty("after"), signature);
            }
        foreach (var format in new[] { "text", "md" })
        {
            var rendered = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", format, .. reusedMode]);
            Assert.StartsWith("exit: 0\n", rendered);
            Assert.Contains("Worker.Run (signature changed)", rendered);
            Assert.Contains("Sink.Keep", rendered);
            Assert.Equal(rendered, await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", format, .. reusedMode]));
        }
    }

    private static void AssertSignature(JsonElement side, string signature) =>
        Assert.Equal("public static " + signature + " -> void", side.GetProperty("signature").GetString());

    private static JsonDocument Parse(string output)
    {
        Assert.StartsWith("exit: 0\n", output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }
}
