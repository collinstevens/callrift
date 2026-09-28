using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

public static class StartupHook
{
    private static int initialized;

    public static void Initialize()
    {
        var directory = Environment.GetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE");
        var assembly = Assembly.GetEntryAssembly()?.GetName().Name;
        var captureSdk = Environment.GetEnvironmentVariable("CALLRIFT_BENCHMARK_CAPTURE_SDK") == "1";
        if (string.IsNullOrEmpty(directory) || assembly is not ("callrift" or "Callrift.MSBuild" or "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost")
            && !(captureSdk && assembly is "dotnet" or "MSBuild")) return;
        if (Interlocked.Exchange(ref initialized, 1) != 0) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            using var process = Process.GetCurrentProcess();
            var result = new
            {
                ProcessId = Environment.ProcessId,
                Assembly = assembly,
                Runtime = RuntimeInformation.FrameworkDescription,
                ManagedAllocatedBytes = allocated,
                Collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
                ElapsedMilliseconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds,
                CpuMilliseconds = process.TotalProcessorTime.TotalMilliseconds,
                PeakWorkingSetBytes = process.PeakWorkingSet64,
                ExitCode = Environment.ExitCode
            };
            var path = Path.Combine(directory, $"process-{Environment.ProcessId}.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(result));
            File.Move(path + ".tmp", path);
        };
    }
}
