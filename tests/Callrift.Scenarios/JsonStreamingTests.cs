using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class JsonStreamingTests
{
    [Theory]
    [InlineData("diff")]
    [InlineData("tree")]
    [InlineData("reach")]
    public async Task StreamPreservesJsonSchemaUnicodeAndCycleReferences(string command)
    {
        var label = string.Concat(Enumerable.Repeat("é漢字🙂\"\\\r\n", 7000));
        var result = new DiffResult([
            new DiffNode("root", label, '~', [
                new DiffNode("cycle", "Root", ' ', []) { Omission = new Omission("cycle", "root") }
            ])
        ], [new AnalysisDiagnostic("unicode", "é漢字🙂")], true)
        { Command = command };
        using var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };

        await JsonRenderer.WriteAsync(result, output);

        var actual = output.ToString();
        Assert.Equal(JsonRenderer.Render(result), actual);
        Assert.DoesNotContain('\r', actual);
        Assert.EndsWith("\n", actual);
        using var document = JsonDocument.Parse(actual);
        var nodes = document.RootElement.GetProperty(command == "reach" ? "paths" : "trees");
        Assert.Equal(label, nodes[0].GetProperty("label").GetString());
        Assert.Equal("n0", nodes[0].GetProperty("children")[0].GetProperty("omission").GetProperty("referenceId").GetString());
        await output.WriteAsync("still open");
        Assert.EndsWith("still open", output.ToString());
    }

    [Fact]
    public async Task WritesBeforeEnumeratingTheEntireResult()
    {
        var nodes = new TrackingNodes(20000);
        using var output = new TrackingWriter(() => nodes.Visited);

        await JsonRenderer.WriteAsync(new DiffResult(nodes, [], true), output);

        Assert.InRange(output.FirstWrittenAt, 1, nodes.Count - 1);
        Assert.Equal(nodes.Count, nodes.Visited);
        Assert.True(output.LargestWrite < output.CharactersWritten);
    }

    [Fact]
    public async Task CancellationDuringOutputStopsTraversal()
    {
        var nodes = new TrackingNodes(20000);
        using var cancellation = new CancellationTokenSource();
        using var output = new TrackingWriter(() => nodes.Visited, cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JsonRenderer.WriteAsync(new DiffResult(nodes, [], true), output, cancellation.Token));

        Assert.InRange(nodes.Visited, 1, nodes.Count - 1);
    }

    private sealed class TrackingNodes(int count) : IReadOnlyList<DiffNode>
    {
        public int Count => count;
        public int Visited { get; private set; }
        public DiffNode this[int index] => new(index.ToString(CultureInfo.InvariantCulture), "Entry.é漢字", '~', []);

        public IEnumerator<DiffNode> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                Visited++;
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TrackingWriter(Func<int> visited, Action? written = null) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public int FirstWrittenAt { get; private set; } = -1;
        public long CharactersWritten { get; private set; }
        public int LargestWrite { get; private set; }

        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FirstWrittenAt < 0) FirstWrittenAt = visited();
            CharactersWritten += buffer.Length;
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
            written?.Invoke();
            return Task.CompletedTask;
        }
    }
}
