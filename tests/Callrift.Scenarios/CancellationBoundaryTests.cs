using System.Collections;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class CancellationBoundaryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Entry")]
    public void CancelledQueriesDoNotReturnResults(string? target)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new QueryRequest("unused") { Options = new DiffOptions { Entries = ["Entry"] }, Target = target };
        Assert.ThrowsAny<OperationCanceledException>(() => CallQueries.Query(Graph([]), request, cancellation.Token));
    }

    [Fact]
    public async Task CancellationAtAnalysisCompletionStopsDiff()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new CallriftService(new BoundaryProvider(Graph([]), cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DiffAsync(
            new SourceSnapshot("before", []), new SourceSnapshot("after", []), new DiffOptions(), cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Entry")]
    public void CancellationStopsReadingFurtherCalls(string? target)
    {
        using var cancellation = new CancellationTokenSource();
        var request = new QueryRequest("unused") { Options = new DiffOptions { Entries = ["Entry"] }, Target = target };
        var graph = Graph(new CancellingCalls(cancellation));
        Assert.ThrowsAny<OperationCanceledException>(() => CallQueries.Query(graph, request, cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationStopsDiffCallGraphTraversal(bool bodyChanged)
    {
        using var cancellation = new CancellationTokenSource();
        var before = Graph(new CancellingCalls(cancellation));
        var after = before with
        {
            Members = new Dictionary<string, Member>
            {
                ["Entry"] = before.Members["Entry"] with { BodyFingerprint = bodyChanged ? "changed" : null }
            }
        };
        var service = new CallriftService(new SnapshotProvider(before, after));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DiffAsync(
            new SourceSnapshot("before", []), new SourceSnapshot("after", []), new DiffOptions(), cancellation.Token));
    }

    private static CallGraph Graph(IReadOnlyList<CallStep> calls) => new(new Dictionary<string, Member>
    {
        ["Entry"] = new Member("Entry", "Entry", "Entry", "Entry", new SourceLocation("Flow.cs", 1, 1), true, calls)
    }, new Dictionary<string, IReadOnlyList<string>>(), []);

    private sealed class BoundaryProvider(CallGraph graph, CancellationTokenSource cancellation) : IAnalysisProvider
    {
        private int analyzed;

        public Task<CallGraph> AnalyzeAsync(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default)
        {
            if (++analyzed == 2) cancellation.Cancel();
            return Task.FromResult(graph);
        }
    }

    private sealed class SnapshotProvider(CallGraph before, CallGraph after) : IAnalysisProvider
    {
        public Task<CallGraph> AnalyzeAsync(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot.Name == "before" ? before : after);
    }

    private sealed class CancellingCalls(CancellationTokenSource cancellation) : IReadOnlyList<CallStep>
    {
        public int Count => 3;

        public CallStep this[int index] => throw new NotSupportedException();

        public IEnumerator<CallStep> GetEnumerator()
        {
            yield return new CallStep("call", "External.First", "First", false, new SourceLocation("Flow.cs", 2, 1), []);
            cancellation.Cancel();
            yield return new CallStep("call", "External.Second", "Second", false, new SourceLocation("Flow.cs", 3, 1), []);
            throw new InvalidOperationException("The query kept reading calls after cancellation.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
