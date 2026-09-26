using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class GitCancellationTests
{
    [Theory]
    [InlineData("command")]
    [InlineData("snapshot")]
    [InlineData("blobs")]
    [InlineData("empty-blobs")]
    public async Task PrecancelledOperationsDoNotRequireAnAccessibleRepository(string operation)
    {
        var missing = Path.Combine(Path.GetTempPath(), "callrift-cancelled-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(missing));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new GitRepository(missing);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "command" => GitRepository.RunAsync(missing, ["status", "--porcelain"], cancellation.Token),
            "snapshot" => repository.ReadSnapshotAsync("HEAD", cancellationToken: cancellation.Token),
            "blobs" => repository.ReadBlobsAsync([new GitEntry("Flow.cs", new string('0', 40))], cancellation.Token),
            "empty-blobs" => repository.ReadBlobsAsync([], cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });
        Assert.False(Directory.Exists(missing));
    }
}
