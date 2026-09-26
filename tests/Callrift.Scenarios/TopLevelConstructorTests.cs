using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class TopLevelConstructorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PartialProgramConstructorsBindAcrossTopLevelFiles(bool separateFile, bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var before = new Dictionary<string, string> { ["App.csproj"] = project, ["Program.cs"] = "public partial class Program { }" };
        if (separateFile) before["A.cs"] = "_ = new Program();";
        else before["Program.cs"] = "_ = new Program(); " + before["Program.cs"];
        var after = new Dictionary<string, string>(before) { ["Program.cs"] = before["Program.cs"].Replace("Program { }", "Program { public Program() {} }", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("top-level-constructor", "The partial Program constructor retains its implicit object base call across source files.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        using var diff = Parse(await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--entry", "new Program", "--externals", "--format", "json", .. mode]));
        Assert.False(diff.RootElement.GetProperty("hasChanges").GetBoolean());
        Assert.Empty(diff.RootElement.GetProperty("trees").EnumerateArray());
        foreach (var revision in new[] { fixture.Before, fixture.After })
        {
            using var tree = Parse(await fixture.RunAsync(["tree", revision, "--entry", "new Program", "--externals", "--format", "json", .. mode]));
            var constructor = Assert.Single(tree.RootElement.GetProperty("trees").EnumerateArray());
            Assert.Equal("new Program", constructor.GetProperty("label").GetString());
            var baseCall = Assert.Single(constructor.GetProperty("children").EnumerateArray());
            Assert.Equal("new Object", baseCall.GetProperty("label").GetString());
            Assert.Equal("resolved", baseCall.GetProperty("after").GetProperty("binding").GetString());
        }
    }

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        return document;
    }
}
