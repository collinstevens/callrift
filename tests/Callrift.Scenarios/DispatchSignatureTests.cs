using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class DispatchSignatureTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ImplementationSignatureChangesRemainVisible(bool explicitContractEntry, bool multiple)
    {
        const string source = "interface IWorker { void Run(int value); } class Worker : IWorker { public void Run(int value) => Sink.Keep(); } static class Entry { public static void Run(IWorker worker) => worker.Run(1); } static class Sink { public static void Keep() {} }";
        var before = source + (multiple ? " class Other : IWorker { public void Run(int value) {} }" : "");
        var after = before.Replace("public void Run(int value) =>", "public void Run(int renamed) =>", StringComparison.Ordinal);
        var result = await Diff(before, after, explicitContractEntry ? "IWorker.Run" : "Entry.Run");
        var nodes = Flatten(result.Trees).ToArray();
        var contract = Assert.Single(nodes, node => node.Before?.SymbolId == "source::IWorker.Run(int)");
        Assert.Equal(' ', contract.Mark);
        Assert.Equal(contract.Before?.Signature, contract.After?.Signature);
        var implementation = Assert.Single(nodes, node => node.Before?.SymbolId == "source::Worker.Run(int)");
        Assert.Equal("dispatchTarget", implementation.Kind);
        Assert.Equal('~', implementation.Mark);
        Assert.Equal("signature changed", implementation.Detail);
        Assert.Equal("public Worker.Run(int value) -> void", implementation.Before?.Signature);
        Assert.Equal("public Worker.Run(int renamed) -> void", implementation.After?.Signature);
        var kept = Assert.Single(implementation.Children);
        Assert.Equal("Sink.Keep", kept.Label);
        Assert.Equal(' ', kept.Mark);
        using var json = JsonDocument.Parse(JsonRenderer.Render(result));
        Assert.Contains("public Worker.Run(int renamed) -> void", json.RootElement.ToString());
        Assert.Contains("⇢ Worker.Run (signature changed)", DiffRenderer.Render(result, new DiffOptions()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContractSignatureChangesRemainVisible(bool multiple)
    {
        const string source = "interface IWorker { void Run(int value); } class Worker : IWorker { public void Run(int value) => Sink.Keep(); } static class Entry { public static void Run(IWorker worker) => worker.Run(1); } static class Sink { public static void Keep() {} }";
        var before = source + (multiple ? " class Other : IWorker { public void Run(int value) {} }" : "");
        var after = before.Replace("void Run(int value);", "void Run(int renamed);", StringComparison.Ordinal);
        var result = await Diff(before, after, "Entry.Run");
        var contract = Assert.Single(Flatten(result.Trees), node => node.Before?.SymbolId == "source::IWorker.Run(int)");
        Assert.Equal('~', contract.Mark);
        Assert.Equal("signature changed", contract.Detail);
        Assert.Equal("public abstract IWorker.Run(int value) -> void", contract.Before?.Signature);
        Assert.Equal("public abstract IWorker.Run(int renamed) -> void", contract.After?.Signature);
        Assert.All(Flatten(contract.Children), node => Assert.Equal(' ', node.Mark));
    }

    private static async Task<DiffResult> Diff(string before, string after, string entry)
    {
        var provider = new SourceOnlyAnalysisProvider();
        Task<CallGraph> Analyze(string source) => provider.AnalyzeAsync(new SourceSnapshot("fixture", [new SourceFile("Flow.cs", source, source)]), new AnalysisOptions());
        var left = await Analyze(before);
        var right = await Analyze(after);
        Assert.Empty(left.Diagnostics);
        Assert.Empty(right.Diagnostics);
        return CallriftService.Compare(left, right, new DiffOptions { Entries = [entry] });
    }

    private static IEnumerable<DiffNode> Flatten(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}
