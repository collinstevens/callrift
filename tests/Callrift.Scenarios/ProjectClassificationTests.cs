using Xunit;

namespace Callrift.Scenarios;

public sealed class ProjectClassificationTests
{
    [Theory]
    [InlineData("<IsTestProject>false</IsTestProject>", "<IsTestProject>true</IsTestProject>", "tests/Host", true, false)]
    [InlineData("<IsTestProject Condition=\"'$(OptionalTests)' == 'true'\">true</IsTestProject>", "<IsTestProject>false</IsTestProject>", "src/Host", true, true)]
    [InlineData("<OutputType>Library</OutputType>", "<OutputType>Exe</OutputType>", "tests/Host", false, true)]
    [InlineData("<ProjectType>Test</ProjectType><OutputType>Exe</OutputType>", "", "tests/Host", false, true)]
    [InlineData("<OutputType>Exe</OutputType>", "<IsTestProject Condition=\"'$(OptionalTests)' == 'true'\">true</IsTestProject>", "tests/Host", true, true)]
    public async Task LiteralOverridesAndConditionalMetadataStayDistinct(string metadata, string defaults, string directory, bool included, bool inferred)
    {
        var before = new Dictionary<string, string>
        {
            ["Directory.Build.props"] = "<Project><PropertyGroup>" + defaults + "</PropertyGroup></Project>",
            [directory + "/Host.csproj"] = "<Project><PropertyGroup>" + metadata + "</PropertyGroup></Project>",
            [directory + "/Host.cs"] = "class Host { public void Entry() => Worker.Run(); }",
            ["src/Worker.cs"] = "static class Worker { public static void Run() { Before(); } static void Before() {} static void After() {} }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["src/Worker.cs"] = before["src/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("test-metadata", "Literal project overrides win and conditional test metadata is reported as inferred.", before, after, []));
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        Assert.Contains("Worker.Before", output);
        Assert.Contains("Worker.After", output);
        if (included) Assert.Contains("Host.Entry", output);
        else Assert.DoesNotContain("Host.Entry", output);
        if (inferred) Assert.Contains("test-project-inferred", output);
        else Assert.DoesNotContain("test-project-inferred", output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SharedTestSettingsExcludeChecksAndKeepApplications(bool workspace, bool includeTests)
    {
        const string library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        const string reference = "<ItemGroup><ProjectReference Include=\"../../src/Core/Core.csproj\" /></ItemGroup>";
        var before = new Dictionary<string, string>
        {
            ["App.slnx"] = "<Solution><Project Path=\"src/Core/Core.csproj\" /><Project Path=\"test/Checks/Checks.csproj\" /><Project Path=\"test/Sample/Sample.csproj\" /><Project Path=\"src/Latest/Latest.csproj\" /></Solution>",
            ["Directory.Build.targets"] = "<Project><Import Project=\"$(MSBuildThisFileDirectory)eng/$(ProjectType).targets\" Condition=\"'$(ProjectType)' != ''\" /></Project>",
            ["eng/Test.targets"] = "<Project><ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.3\" /></ItemGroup></Project>",
            ["src/Core/Core.csproj"] = library,
            ["src/Core/Worker.cs"] = "public static class Worker { public static void Run() { Before(); } static void Before() {} static void After() {} }",
            ["test/Checks/Checks.csproj"] = library.Replace("</PropertyGroup>", "<ProjectType>Test</ProjectType></PropertyGroup>", StringComparison.Ordinal).Replace("</Project>", reference + "</Project>", StringComparison.Ordinal),
            ["test/Checks/Checks.cs"] = "public class Checks { public void Scenario() => Worker.Run(); }",
            ["test/Sample/Sample.csproj"] = library.Replace("</PropertyGroup>", "<OutputType>Exe</OutputType></PropertyGroup>", StringComparison.Ordinal).Replace("</Project>", reference + "</Project>", StringComparison.Ordinal),
            ["test/Sample/Sample.cs"] = "public class Sample { public static void Main() => Worker.Run(); }",
            ["src/Latest/Latest.csproj"] = library.Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../Core/Core.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal),
            ["src/Latest/Latest.cs"] = "public class Latest { public void Run() => Worker.Run(); }"
        };
        var after = new Dictionary<string, string>(before)
        {
            ["src/Core/Worker.cs"] = before["src/Core/Worker.cs"].Replace("Before();", "After();", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("shared-test-settings", "Test checks are excluded while application callers under test directories and Latest remain visible.", before, after, []));
        string[] mode = workspace ? ["--solution", "App.slnx"] : [];
        string[] tests = includeTests ? ["--tests"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, .. tests, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("Sample.Main", output);
            Assert.Contains("Latest.Run", output);
            Assert.Contains("Worker.Before", output);
            Assert.Contains("Worker.After", output);
            if (includeTests) Assert.Contains("Checks.Scenario", output);
            else Assert.DoesNotContain("Checks.Scenario", output);
        }
    }
}
