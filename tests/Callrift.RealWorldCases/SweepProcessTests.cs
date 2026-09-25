using System.Diagnostics;
using Xunit;

namespace Callrift.RealWorldCases;

public sealed class SweepProcessTests
{
    [Fact]
    public async Task AbruptExitDoesNotTerminateTheSweepHost()
    {
        var result = await SweepProcess.RunAsync(Command("[Console]::Out.WriteLine('before crash'); [Console]::Out.Flush(); [System.Diagnostics.Process]::GetCurrentProcess().Kill()"), TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("before crash", result.Output);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task LargeOutputIsDrainedAndBoundedOnBothStreams()
    {
        var result = await SweepProcess.RunAsync(Command("[Console]::Out.Write(('x' * 100000)); [Console]::Error.Write(('y' * 100000)); exit 37"), TimeSpan.FromSeconds(30));
        Assert.Equal(37, result.ExitCode);
        Assert.StartsWith(new string('x', 16384), result.Output);
        Assert.StartsWith(new string('y', 16384), result.Error);
        Assert.EndsWith("[output truncated]", result.Output);
        Assert.InRange(result.Output.Length, 16384, 16410);
        Assert.InRange(result.Error.Length, 16384, 16410);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task DeadlineTerminatesTheWorkerAndItsChild()
    {
        var marker = Path.Combine(Path.GetTempPath(), "callrift-sweep-pids-" + Guid.NewGuid().ToString("N"));
        var start = Command("$childStart = [System.Diagnostics.ProcessStartInfo]::new('pwsh'); $childStart.UseShellExecute = $false; $childStart.CreateNoWindow = $true; $childStart.ArgumentList.Add('-NoProfile'); $childStart.ArgumentList.Add('-Command'); $childStart.ArgumentList.Add('Start-Sleep -Seconds 300'); $child = [System.Diagnostics.Process]::Start($childStart); [IO.File]::WriteAllText($env:CALLRIFT_SWEEP_TEST_PIDS, ($PID.ToString() + ',' + $child.Id.ToString())); Start-Sleep -Seconds 300");
        start.Environment["CALLRIFT_SWEEP_TEST_PIDS"] = marker;
        try
        {
            var result = await SweepProcess.RunAsync(start, TimeSpan.FromSeconds(10));
            Assert.True(result.TimedOut);
            var ids = (await File.ReadAllTextAsync(marker)).Split(',').Select(int.Parse).ToArray();
            Assert.Equal(2, ids.Length);
            foreach (var id in ids) await AssertStoppedAsync(id);
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task CallerCancellationTerminatesTheStartedWorker()
    {
        var marker = Path.Combine(Path.GetTempPath(), "callrift-sweep-cancel-" + Guid.NewGuid().ToString("N"));
        var start = Command("[IO.File]::WriteAllText($env:CALLRIFT_SWEEP_TEST_PIDS, $PID.ToString()); Start-Sleep -Seconds 300");
        start.Environment["CALLRIFT_SWEEP_TEST_PIDS"] = marker;
        using var cancellation = new CancellationTokenSource();
        var running = SweepProcess.RunAsync(start, TimeSpan.FromSeconds(30), cancellation.Token);
        try
        {
            using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(marker)) await Task.Delay(20, ready.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            await AssertStoppedAsync(int.Parse(await File.ReadAllTextAsync(marker)));
        }
        finally
        {
            cancellation.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            File.Delete(marker);
        }
    }

    private static ProcessStartInfo Command(string script)
    {
        var start = new ProcessStartInfo("pwsh");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task AssertStoppedAsync(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(process.HasExited);
        }
        catch (ArgumentException) { }
    }
}
