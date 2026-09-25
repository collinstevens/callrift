using System.Diagnostics;
using System.Text;

namespace Callrift.RealWorldCases;

public sealed record SweepProcessResult(int ExitCode, string Output, string Error, bool TimedOut);

public static class SweepProcess
{
    public static async Task<SweepProcessResult> RunAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start sweep worker.");
        var output = ReadAsync(process.StandardOutput, deadline.Token);
        var error = ReadAsync(process.StandardError, deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error).WaitAsync(deadline.Token);
            return new(process.ExitCode, await output, await error, false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Stop(process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await ObserveCancellationAsync(output, error);
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, "", "Sweep worker exceeded its time limit.", true);
        }
        finally
        {
            Stop(process);
        }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
    }

    private static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        var truncated = false;
        int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            var retained = Math.Min(length, Math.Max(0, 16384 - text.Length));
            text.Append(buffer, 0, retained);
            truncated |= retained < length;
        }
        return text.ToString() + (truncated ? "\n[output truncated]" : "");
    }

    private static async Task ObserveCancellationAsync(params Task<string>[] tasks)
    {
        foreach (var task in tasks)
            try { await task; } catch (OperationCanceledException) { }
    }
}
