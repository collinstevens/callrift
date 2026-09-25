using Xunit;

namespace Callrift.Scenarios;

public sealed class XunitExecutableClassificationTests
{
    [Theory]
    [InlineData("xunit.v3.mtp-v1")]
    [InlineData("xunit.v3.mtp-v2")]
    [InlineData("xunit.v3.mtp-off")]
    [InlineData("xunit.v3.aot")]
    [InlineData("xunit.v3.aot.mtp-v2")]
    [InlineData("xunit.v3.aot.mtp-off")]
    public async Task TestExecutablesAreOptInAndApplicationExecutablesStayVisible(string package)
    {
        var before = new Dictionary<string, string>
        {
            ["tests/Checks/Checks.csproj"] = "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include=\"" + package + "\" /></ItemGroup></Project>",
            ["tests/Checks/Checks.cs"] = "class Checks { public static void Main() => Worker.Run(); }",
            ["tests/Sample/Sample.csproj"] = "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>",
            ["tests/Sample/Sample.cs"] = "class Sample { public static void Main() => Worker.Run(); }",
            ["src/Worker.cs"] = "static class Worker { public static void Run() { Before(); } static void Before() {} static void After() {} }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["src/Worker.cs"] = before["src/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("xunit-executable", "Executable xUnit packages identify test projects without hiding application entry points.", before, after, []));
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("Sample.Main", output);
            Assert.DoesNotContain("Checks.Main", output);
            Assert.Contains("Worker.Before", output);
            Assert.Contains("Worker.After", output);
            var withTests = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--tests", "--format", format]);
            Assert.True(withTests.StartsWith("exit: 0\n", StringComparison.Ordinal), withTests);
            Assert.Contains("Checks.Main", withTests);
            Assert.Contains("Sample.Main", withTests);
        }
    }
}
