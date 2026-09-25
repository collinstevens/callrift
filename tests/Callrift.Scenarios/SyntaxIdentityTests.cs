using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class SyntaxIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FormattingDoesNotChangeGuardsOrUnresolvedTargets(bool workspace, bool unresolved)
    {
        var body = unresolved ? "Missing.Create().Send();" : "if (ready && Check()) Work(); foreach (System.String item in new System.String[0]) Work();";
        var formatted = unresolved ? "Missing . Create() . Send();" : "if (ready&&Check()) Work(); foreach (System . String item in new System.String[0]) Work();";
        var source = "public class Flow { public void Run(bool ready) { " + body + " } bool Check() => true; void Work() {} }";
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = source
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace(body, formatted, StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("syntax-formatting", "Formatting preserves branch and unresolved target identity.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.DoesNotContain("Flow.Run", output);
            if (format != "json") continue;
            using var document = Parse(output);
            Assert.False(document.RootElement.GetProperty("hasChanges").GetBoolean());
            Assert.Empty(document.RootElement.GetProperty("trees").EnumerateArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiteralWhitespaceRemainsVisibleAndSignificant(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public class Flow { public void Run(string value) { if (value == \"a  b\") Work(); Missing.Choose(\"a  b\").Send(); } void Work() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("a  b", "a b", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("literal-whitespace", "Whitespace inside string literals changes guards and unresolved fluent targets.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", format, "--context", "all"]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("a  b", output);
            Assert.Contains("a b", output);
            if (format != "json") continue;
            using var document = Parse(output);
            Assert.True(document.RootElement.GetProperty("hasChanges").GetBoolean());
            var children = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("children").EnumerateArray().ToArray();
            Assert.Contains(children, node => node.GetProperty("change").GetString() == "removed" && node.GetProperty("label").GetString() == "if (value == \"a  b\")");
            Assert.Contains(children, node => node.GetProperty("change").GetString() == "added" && node.GetProperty("label").GetString() == "if (value == \"a b\")");
            Assert.Contains(children, node => node.GetProperty("change").GetString() == "removed" && node.GetProperty("label").GetString() == "? Missing.Choose(\"a  b\").Send");
            Assert.Contains(children, node => node.GetProperty("change").GetString() == "added" && node.GetProperty("label").GetString() == "? Missing.Choose(\"a b\").Send");
        }
    }

    private static JsonDocument Parse(string output) => JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
}
