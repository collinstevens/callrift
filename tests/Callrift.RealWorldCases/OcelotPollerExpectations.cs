using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotPollerExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored) Assert.Empty(result.Diagnostics);
        else Assert.Equal(1083, result.Diagnostics.Count);
        Assert.Equal(focused ? 5 : 4, result.Trees.Count);
        var poll = Assert.Single(result.Trees, node => node.Label == "FileConfigurationPoller.PollAsync");
        Assert.Contains(poll.Children, node => node.Label == "FileConfigurationPoller.TryEnterPolling" && node.Mark == '+');
        var guarded = Assert.Single(poll.Children, node => node.Label == "try" && node.After is not null);
        var changed = Assert.Single(guarded.Children, node => node.Label == "if (asJson != _previousAsJson)");
        Assert.Contains(changed.Children, node => node.Label == "Response.get_IsError");
        var success = Assert.Single(changed.Children, node => node.Label == "if (!config.IsError)");
        Assert.Equal("Response<T>.get_Data", success.Children[0].Label);
        Assert.StartsWith("IInternalConfigurationRepository.AddOrReplace", success.Children[1].Label);
        var release = Assert.Single(poll.Children, node => node.Label == "finally" && node.Mark == '+');
        Assert.Equal("FileConfigurationPoller.ExitPolling", Assert.Single(release.Children).Label);
        if (!focused)
        {
            Assert.Contains(result.Trees, node => node.Label == (restored ? "FileConfigurationPoller.Dispose" : "WatchKube.Dispose"));
            return;
        }
        var stop = Assert.Single(result.Trees, node => node.Label == "FileConfigurationPoller.StopAsync");
        Assert.Equal("signature changed", stop.Detail);
        Assert.StartsWith("public async ", stop.After!.Signature);
        var nullTimer = Assert.Single(stop.Children, node => node.Label == "if (_timer is null)");
        Assert.Equal('-', nullTimer.Mark);
        Assert.Equal("Task.get_CompletedTask", Assert.Single(nullTimer.Children).Label);
        Assert.Contains(stop.Children, node => node.Label == "Interlocked.Exchange" && node.Mark == '+');
        Assert.Contains(stop.Children, node => node.Label == "timer.Change" && node.Mark == '+');
    }
}
