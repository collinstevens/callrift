using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstructorDiscoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddedAndRemovedTypesExposeTheirDefaultConstructors(bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        await using var fixture = await GitFixture.CreateAsync(new Scenario("constructor-discovery", "Adding or removing a type adds or removes its available default constructor.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = "public class Removed { }" },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = "public class Added { }" }, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--externals", "--format", "json", .. mode]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.True(document.RootElement.GetProperty("hasChanges").GetBoolean());
        var roots = document.RootElement.GetProperty("trees").EnumerateArray().ToArray();
        Assert.Equal(new[] { "new Added", "new Removed" }, roots.Select(node => node.GetProperty("label").GetString()).Order(StringComparer.Ordinal));
        foreach (var root in roots)
        {
            var added = root.GetProperty("label").GetString() == "new Added";
            Assert.Equal(added ? "added" : "removed", root.GetProperty("change").GetString());
            var side = root.GetProperty(added ? "after" : "before");
            Assert.Equal(added ? "Added.Added() -> void" : "Removed.Removed() -> void", side.GetProperty("signature").GetString());
            Assert.Equal("Flow.cs", side.GetProperty("definition").GetProperty("path").GetString());
            var baseCall = Assert.Single(root.GetProperty("children").EnumerateArray());
            Assert.Equal("new Object", baseCall.GetProperty("label").GetString());
        }
    }
}
