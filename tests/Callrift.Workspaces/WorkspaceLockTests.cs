using Callrift.MSBuild;
using Callrift.Scenarios;
using Xunit;

namespace Callrift.Workspaces;

public sealed class WorkspaceLockTests : IDisposable
{
    private readonly string directory = Directory.CreateDirectory(FixtureDirectory.CreatePath("callrift-lock-")).FullName;

    [Fact]
    public async Task ReleasedLeaseAllowsTheNextWaiter()
    {
        var path = Path.Combine(directory, "analysis.lock");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = await MSBuildAnalysisProvider.AcquireAsync(path, cancellation.Token);
        var waiting = MSBuildAnalysisProvider.AcquireAsync(path, cancellation.Token);
        Assert.False(waiting.IsCompleted);
        first.Dispose();
        using var second = await waiting;
        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        second.Dispose();
        using var third = await MSBuildAnalysisProvider.AcquireAsync(path, cancellation.Token);
    }

    [Fact]
    public async Task ContentionWaitsPastTheFormerRetryLimit()
    {
        var path = Path.Combine(directory, "analysis.lock");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var holder = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var waiting = MSBuildAnalysisProvider.AcquireAsync(path, cancellation.Token);
        await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(90), cancellation.Token));
        if (waiting.IsCompleted)
        {
            using var unexpected = await waiting;
            Assert.Fail("The lease was acquired while another handle still held the lock.");
        }
        holder.Dispose();
        using var acquired = await waiting;
    }

    [Fact]
    public async Task CancellationStopsAContendedWaitWithoutReleasingTheHolder()
    {
        var path = Path.Combine(directory, "analysis.lock");
        using var holder = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = MSBuildAnalysisProvider.AcquireAsync(path, cancellation.Token);
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        holder.Dispose();
        using var acquired = await MSBuildAnalysisProvider.AcquireAsync(path, CancellationToken.None);
    }

    [Fact]
    public async Task PrecancelledRequestDoesNotCreateALockFile()
    {
        var path = Path.Combine(directory, "analysis.lock");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MSBuildAnalysisProvider.AcquireAsync(path, cancellation.Token));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidParentFailsWithoutRetrying(bool parentIsFile)
    {
        var parent = Path.Combine(directory, "parent");
        if (parentIsFile) await File.WriteAllTextAsync(parent, "not a directory");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            MSBuildAnalysisProvider.AcquireAsync(Path.Combine(parent, "analysis.lock"), cancellation.Token));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
