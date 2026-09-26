using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Callrift.Core;
using Callrift.MSBuild;

namespace Callrift.Scenarios;

public static class WorkspaceFixture
{
    public static async Task<(CallGraph Before, CallGraph After)> AnalyzeAsync(Scenario scenario, bool includeTests = false)
    {
        var directory = FixtureDirectory.CreatePath("callrift-analysis-fixture-");
        try
        {
            var before = await AnalyzeAsync(Path.Combine(directory, "before"), scenario.Before, includeTests);
            var after = await AnalyzeAsync(Path.Combine(directory, "after"), scenario.After, includeTests);
            return (before, after);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task<CallGraph> AnalyzeAsync(string root, IReadOnlyDictionary<string, string> files, bool includeTests)
    {
        foreach (var file in files)
        {
            var path = Path.GetFullPath(file.Key, root);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Fixture path escapes the workspace.", nameof(files));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Value.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        }
        var options = new MSBuildOptions("App.csproj", NoRestore: true);
        await RunAsync(root, ["restore", options.Target, "--nologo", "-p:Configuration=" + options.Configuration]);
        var requestPath = root + ".request.json";
        var resultPath = root + ".graph.json";
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new WorkspaceRequest(root, options, includeTests, resultPath)));
        var assembly = typeof(MSBuildAnalysisProvider).Assembly.Location;
        var worker = Path.Combine(Path.GetDirectoryName(assembly)!, "msbuild", Path.GetFileName(assembly));
        await RunAsync(root, [worker, requestPath]);
        return JsonSerializer.Deserialize<CallGraph>(await File.ReadAllTextAsync(resultPath), new JsonSerializerOptions { MaxDepth = 1024 })
            ?? throw new InvalidOperationException("Workspace worker returned no graph.");
    }

    private static async Task RunAsync(string root, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        start.Environment.Remove("MSBUILD_EXE_PATH");
        start.Environment.Remove("MSBuildSDKsPath");
        start.Environment.Remove("MSBuildExtensionsPath");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the .NET SDK.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} exceeded three minutes.\nstdout:\n{await stdout}\nstderr:\n{await stderr}");
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} failed ({process.ExitCode}).\nstdout:\n{output}\nstderr:\n{error}");
    }
}
