using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class TopLevelConstructorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task PartialProgramConstructorsBindAcrossTopLevelFiles(bool separateFile) => VerifyPartialProgramConstructorsBindAcrossTopLevelFiles(separateFile, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspacePartialProgramConstructorsBindAcrossTopLevelFiles(bool separateFile) => VerifyPartialProgramConstructorsBindAcrossTopLevelFiles(separateFile, true);

    private static async Task VerifyPartialProgramConstructorsBindAcrossTopLevelFiles(bool separateFile, bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var before = new Dictionary<string, string> { ["App.csproj"] = project, ["Program.cs"] = "public partial class Program { }" };
        if (separateFile) before["A.cs"] = "_ = new Program();";
        else before["Program.cs"] = "_ = new Program(); " + before["Program.cs"];
        var after = new Dictionary<string, string>(before) { ["Program.cs"] = before["Program.cs"].Replace("Program { }", "Program { public Program() {} }", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("top-level-constructor", "The partial Program constructor retains its implicit object base call across source files.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["new Program"], IncludeExternals = true };
        using var diff = Parse(await fixture.DiffAsync(options));
        Assert.False(diff.RootElement.GetProperty("hasChanges").GetBoolean());
        Assert.Empty(diff.RootElement.GetProperty("trees").EnumerateArray());
        foreach (var beforeRevision in new[] { true, false })
        {
            using var tree = Parse(await fixture.QueryAsync(options, before: beforeRevision));
            var constructor = Assert.Single(tree.RootElement.GetProperty("trees").EnumerateArray());
            Assert.Equal("new Program", constructor.GetProperty("label").GetString());
            var baseCall = Assert.Single(constructor.GetProperty("children").EnumerateArray());
            Assert.Equal("new Object", baseCall.GetProperty("label").GetString());
            Assert.Equal("resolved", baseCall.GetProperty("after").GetProperty("binding").GetString());
        }
    }

    private static JsonDocument Parse(string output)
    {
        var document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        return document;
    }
}
