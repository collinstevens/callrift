using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class SyntaxIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task FormattingDoesNotChangeGuardsOrUnresolvedTargets(bool unresolved) => VerifyFormattingDoesNotChangeGuardsOrUnresolvedTargets(false, unresolved);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceFormattingDoesNotChangeGuardsOrUnresolvedTargets(bool unresolved) => VerifyFormattingDoesNotChangeGuardsOrUnresolvedTargets(true, unresolved);

    private static async Task VerifyFormattingDoesNotChangeGuardsOrUnresolvedTargets(bool workspace, bool unresolved)
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
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("syntax-formatting", "Formatting preserves branch and unresolved target identity.", before, after, []), workspace);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions());
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = outputs[format];
            Assert.DoesNotContain("Flow.Run", output);
            if (format != "json") continue;
            using var document = Parse(output);
            Assert.False(document.RootElement.GetProperty("hasChanges").GetBoolean());
            Assert.Empty(document.RootElement.GetProperty("trees").EnumerateArray());
        }
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task LiteralWhitespaceRemainsVisibleAndSignificant() => VerifyLiteralWhitespaceRemainsVisibleAndSignificant(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceLiteralWhitespaceRemainsVisibleAndSignificant() => VerifyLiteralWhitespaceRemainsVisibleAndSignificant(true);

    private static async Task VerifyLiteralWhitespaceRemainsVisibleAndSignificant(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public class Flow { public void Run(string value) { if (value == \"a  b\") Work(); Missing.Choose(\"a  b\").Send(); } void Work() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("a  b", "a b", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("literal-whitespace", "Whitespace inside string literals changes guards and unresolved fluent targets.", before, after, []), workspace);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions { Context = -1 });
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = outputs[format];
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

    private static JsonDocument Parse(string output) => JsonDocument.Parse(output);
}
