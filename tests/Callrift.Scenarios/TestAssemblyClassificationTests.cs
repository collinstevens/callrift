using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class TestAssemblyClassificationTests
{
    [Theory]
    [InlineData("MSTest.TestFramework", "", "", false)]
    [InlineData("Microsoft.VisualStudio.TestPlatform.TestFramework", "", "", false)]
    [InlineData("Microsoft.VisualStudio.TestPlatform.TestFramework, Version=14.0.0.0, Culture=neutral", "", "", false)]
    [InlineData("nunit.framework", "", "", false)]
    [InlineData("xunit.core", "", "", false)]
    [InlineData("xunit.v3.core", "", "", false)]
    [InlineData("MSTest.TestFramework", "<IsTestProject>false</IsTestProject>", "", true)]
    [InlineData("MSTest.TestFramework", "", " Condition=\"'$(OptionalTests)' == 'true'\"", true)]
    [InlineData("Microsoft.AspNetCore.TestHost", "", "", true)]
    public async Task FrameworkAssemblyReferencesDistinguishTestsFromApplications(string assembly, string metadata, string condition, bool included)
    {
        var before = new Dictionary<string, string>
        {
            ["checks/Checks.csproj"] = "<Project><PropertyGroup><OutputType>Exe</OutputType>" + metadata
                + "</PropertyGroup><ItemGroup><Reference Include=\"" + assembly + "\"" + condition + " /></ItemGroup></Project>",
            ["checks/Checks.cs"] = "class Checks { public static void Main() => Worker.Run(); }",
            ["tests/Sample/Sample.csproj"] = "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>",
            ["tests/Sample/Sample.cs"] = "class Sample { public static void Main() => Worker.Run(); }",
            ["src/Worker.cs"] = "static class Worker { public static void Run() { Before(); } static void Before() {} static void After() {} }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["src/Worker.cs"] = before["src/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        var scenario = new Scenario("test-assembly-reference",
            "Direct test-framework references identify test executables while explicit applications remain visible.", before, after, []);
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace: false);
        await using var includedFixture = await AnalysisFixture.CreateAsync(scenario, workspace: false, includeTests: true);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions());
        var includedOutputs = await includedFixture.DiffFormatsAsync(new DiffOptions { IncludeTests = true });
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = outputs[format];
            Assert.Contains("Sample.Main", output);
            Assert.Contains("Worker.After", output);
            if (included) Assert.Contains("Checks.Main", output);
            else Assert.DoesNotContain("Checks.Main", output);
            if (format == "json" && condition.Length > 0) Assert.Contains("test-project-inferred", output);
            var withTests = includedOutputs[format];
            Assert.Contains("Checks.Main", withTests);
            Assert.Contains("Sample.Main", withTests);
        }
    }
}
