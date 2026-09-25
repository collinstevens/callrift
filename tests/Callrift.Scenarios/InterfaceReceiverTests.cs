using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class InterfaceReceiverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ObjectMethodCandidatesMustImplementReceiverInterface(bool workspace, bool castFromObject)
    {
        const string source = """
            interface IWorker {}
            class Worker : IWorker { public override string ToString() { Before(); return "worker"; } void Before() {} void After() {} }
            class Unrelated { public override string ToString() { Wrong(); return "other"; } void Wrong() {} }
            class Flow { public string Run(IWorker worker) => worker.ToString(); }
            """;
        var input = castFromObject ? source.Replace("IWorker worker", "object worker", StringComparison.Ordinal)
            .Replace("worker.ToString()", "((IWorker)worker).ToString()", StringComparison.Ordinal) : source;
        await using var fixture = await CreateAsync(input);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] entry = command == "diff" ? [] : ["--entry", "Flow.Run"];
            string[] target = command == "reach" ? ["--to", "Worker.After"] : [];
            var output = await fixture.RunAsync([command, .. revisions, .. entry, .. target, .. mode, "--format", "json"]);
            using var document = Parse(output);
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
            var call = root.GetProperty("children")[0];
            Assert.EndsWith("::Worker.ToString()", Assert.Single(call.GetProperty("after").GetProperty("targetIds").EnumerateArray()).GetString());
            Assert.Contains("Worker.After", output);
            Assert.DoesNotContain("Unrelated", output);
        }
        var impossible = await fixture.RunAsync(["reach", fixture.After, "--entry", "Flow.Run", "--to", "Unrelated.Wrong", .. mode, "--format", "json"]);
        using var unreachable = Parse(impossible);
        Assert.Empty(unreachable.RootElement.GetProperty("paths").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbstractClassConcreteImplementationRemainsPossible(bool workspace)
    {
        const string source = """
            interface IWorker { void Run(); }
            abstract class Worker : IWorker { public void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Handle(IWorker worker) => worker.Run(); }
            """;
        await using var fixture = await CreateAsync(source);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
            string[] entry = command == "diff" ? [] : ["--entry", "Flow.Handle"];
            string[] target = command == "reach" ? ["--to", "Worker.After"] : [];
            var output = await fixture.RunAsync([command, .. revisions, .. entry, .. target, .. mode, "--format", "json"]);
            using var document = Parse(output);
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
            Assert.Contains("Worker.After", output);
            Assert.Equal("possible", root.GetProperty("children")[0].GetProperty("after").GetProperty("dispatch").GetString());
        }
    }

    private static Task<GitFixture> CreateAsync(string source)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = source
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Before();", "After();", StringComparison.Ordinal) };
        return GitFixture.CreateAsync(new Scenario("interface-receiver", "Interface receivers exclude unrelated implementations and retain callable bodies on abstract classes.", before, after, []));
    }

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());
        return document;
    }
}
