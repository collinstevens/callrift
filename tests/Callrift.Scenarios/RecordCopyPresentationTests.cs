using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class RecordCopyPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloneLabelsPreserveIdentitiesLocationsAndCopyOrder(bool workspace)
    {
        const string source = """
            sealed record State
            {
                public State() {}
                private State(State other) { Sink.Before(); }
                public int Value { get; init; }
            }
            static class Entry
            {
                public static State Run(State value) => value with { Value = Sink.Other() };
            }
            static class Sink { public static int Before() => 0; public static int After() => 0; public static int Other() => 0; }
            """;
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("record-copy-presentation", "Record copying has readable labels and original-source locations without changing compiler identities.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var scope = workspace ? "project:App.csproj@net11.0::" : "source::";
        foreach (var command in new[] { "tree", "reach", "diff" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.Before];
            string[] target = command == "reach" ? ["--to", "Sink.Before"] : [];
            foreach (var format in new[] { "text", "md", "json" })
            {
                var output = await fixture.RunAsync([command, .. revisions, "--entry", "Entry.Run", .. target, "--depth", "16", "--externals", "--format", format, .. mode]);
                Assert.StartsWith("exit: 0\n", output);
                Assert.Contains("clone State", output);
                if (format != "json")
                {
                    Assert.DoesNotContain("<Clone>$", output);
                    continue;
                }
                using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
                Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
                var nodes = Flatten(root).ToArray();
                Assert.DoesNotContain(nodes, node => node.GetProperty("label").GetString()!.Contains("<Clone>$", StringComparison.Ordinal));
                var clone = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "clone State");
                var cloneSide = clone.GetProperty("after");
                Assert.Equal(scope + "State.<Clone>$()", cloneSide.GetProperty("symbolId").GetString());
                Assert.Equal("direct", cloneSide.GetProperty("dispatch").GetString());
                AssertLocation(cloneSide.GetProperty("definition"), 1, 1, 6, 2);
                AssertLocation(Assert.Single(cloneSide.GetProperty("callSites").EnumerateArray()), 9, 45, 9, 80);
                var copy = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "new State");
                var copySide = copy.GetProperty("after");
                Assert.Equal(scope + "State..ctor(global::State)", copySide.GetProperty("symbolId").GetString());
                AssertLocation(copySide.GetProperty("definition"), 4, 5, 4, command == "diff" ? 49 : 50);
                AssertLocation(Assert.Single(copySide.GetProperty("callSites").EnumerateArray()), 1, 1, 6, 2);
                if (command == "tree")
                {
                    var labels = nodes.Select(node => node.GetProperty("label").GetString()).ToArray();
                    Assert.True(Array.IndexOf(labels, "clone State") < Array.IndexOf(labels, "new State"));
                    Assert.True(Array.IndexOf(labels, "new State") < Array.IndexOf(labels, "Sink.Before"));
                    Assert.True(Array.IndexOf(labels, "Sink.Before") < Array.IndexOf(labels, "Sink.Other"));
                }
            }
        }
    }

    private static void AssertLocation(JsonElement location, int startLine, int startColumn, int endLine, int endColumn)
    {
        Assert.Equal("Flow.cs", location.GetProperty("path").GetString());
        Assert.Equal(startLine, location.GetProperty("startLine").GetInt32());
        Assert.Equal(startColumn, location.GetProperty("startColumn").GetInt32());
        Assert.Equal(endLine, location.GetProperty("endLine").GetInt32());
        Assert.Equal(endColumn, location.GetProperty("endColumn").GetInt32());
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) => new[] { node }.Concat(node.GetProperty("children").EnumerateArray().SelectMany(Flatten));
}
