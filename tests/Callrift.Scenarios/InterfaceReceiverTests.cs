using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class InterfaceReceiverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task ObjectMethodCandidatesMustImplementReceiverInterface(bool castFromObject) => VerifyObjectMethodCandidatesMustImplementReceiverInterface(false, castFromObject);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceObjectMethodCandidatesMustImplementReceiverInterface(bool castFromObject) => VerifyObjectMethodCandidatesMustImplementReceiverInterface(true, castFromObject);

    private static async Task VerifyObjectMethodCandidatesMustImplementReceiverInterface(bool workspace, bool castFromObject)
    {
        const string source = """
            interface IWorker {}
            class Worker : IWorker { public override string ToString() { Before(); return "worker"; } void Before() {} void After() {} }
            class Unrelated { public override string ToString() { Wrong(); return "other"; } void Wrong() {} }
            class Flow { public string Run(IWorker worker) => worker.ToString(); }
            """;
        var input = castFromObject ? source.Replace("IWorker worker", "object worker", StringComparison.Ordinal)
            .Replace("worker.ToString()", "((IWorker)worker).ToString()", StringComparison.Ordinal) : source;
        await using var fixture = await CreateAsync(input, workspace);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var options = new DiffOptions { Entries = command == "diff" ? [] : ["Flow.Run"] };
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Worker.After" : null);
            using var document = Parse(output);
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
            var call = root.GetProperty("children")[0];
            Assert.EndsWith("::Worker.ToString()", Assert.Single(call.GetProperty("after").GetProperty("targetIds").EnumerateArray()).GetString());
            Assert.Contains("Worker.After", output);
            Assert.DoesNotContain("Unrelated", output);
        }
        var impossible = await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] }, target: "Unrelated.Wrong");
        using var unreachable = Parse(impossible);
        Assert.Empty(unreachable.RootElement.GetProperty("paths").EnumerateArray());
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task AbstractClassConcreteImplementationRemainsPossible() => VerifyAbstractClassConcreteImplementationRemainsPossible(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceAbstractClassConcreteImplementationRemainsPossible() => VerifyAbstractClassConcreteImplementationRemainsPossible(true);

    private static async Task VerifyAbstractClassConcreteImplementationRemainsPossible(bool workspace)
    {
        const string source = """
            interface IWorker { void Run(); }
            abstract class Worker : IWorker { public void Run() { Before(); } void Before() {} void After() {} }
            class Flow { public void Handle(IWorker worker) => worker.Run(); }
            """;
        await using var fixture = await CreateAsync(source, workspace);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var options = new DiffOptions { Entries = command == "diff" ? [] : ["Flow.Handle"] };
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Worker.After" : null);
            using var document = Parse(output);
            var root = Assert.Single(document.RootElement.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray());
            Assert.Equal("Flow.Handle", root.GetProperty("label").GetString());
            Assert.Contains("Worker.After", output);
            Assert.Equal("possible", root.GetProperty("children")[0].GetProperty("after").GetProperty("dispatch").GetString());
        }
    }

    private static Task<AnalysisFixture> CreateAsync(string source, bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = source
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Before();", "After();", StringComparison.Ordinal) };
        return AnalysisFixture.CreateAsync(new Scenario("interface-receiver", "Interface receivers exclude unrelated implementations and retain callable bodies on abstract classes.", before, after, []), workspace);
    }

    private static JsonDocument Parse(string output)
    {
        var document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());
        return document;
    }
}
