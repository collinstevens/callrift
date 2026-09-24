using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Callrift.Core;

namespace Callrift.MSBuild;

public sealed record MSBuildOptions(string Target, string? Framework = null, string Configuration = "Debug", bool NoRestore = false);

public sealed record WorkspaceRequest(string Root, MSBuildOptions Options, bool IncludeTests, string ResultPath);

public sealed class MSBuildAnalysisProvider(MSBuildOptions options) : IAnalysisProvider
{
    public bool RequiresProjectFiles => true;

    public async Task<CallGraph> AnalyzeAsync(SourceSnapshot snapshot, AnalysisOptions analysis, CancellationToken cancellationToken = default)
    {
        if (snapshot.Files.Count == 0) return new CallGraph(new Dictionary<string, Member>(), new Dictionary<string, IReadOnlyList<string>>(), []) { Coverage = WorkspaceCoverage };
        var target = options.Target.Replace('\\', '/');
        if (Path.IsPathRooted(target) || target.Split('/').Any(p => p == "..")) throw new ArgumentException("Workspace target must be a repository-relative path.");
        if (!snapshot.Files.Any(f => f.Path == target)) throw new InvalidOperationException($"Workspace target is missing from the snapshot: {target}");
        var identity = target + "\0" + options.Framework + "\0" + options.Configuration + "\0" + string.Join("\0", snapshot.Files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => f.Path + "\0" + f.ContentId));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var cache = Environment.GetEnvironmentVariable("CALLRIFT_WORKSPACE_CACHE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Callrift", "workspaces");
        var directory = Path.Combine(cache, digest);
        Directory.CreateDirectory(directory);
        using var lease = await AcquireAsync(Path.Combine(directory, "analysis.lock"), cancellationToken);
        var root = Path.Combine(directory, "tree");
        Directory.CreateDirectory(root);
        foreach (var file in snapshot.Files)
        {
            var path = Path.GetFullPath(file.Path, root);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("Snapshot path escapes the workspace.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, file.RawBytes ?? Encoding.UTF8.GetBytes(file.Content), cancellationToken);
        }
        var targetPath = Path.Combine(root, target);
        var restored = Path.Combine(directory, "restored");
        if (options.NoRestore && !File.Exists(restored)) throw new InvalidOperationException("No restored workspace cache exists for this snapshot; run without --no-restore first.");
        if (!options.NoRestore)
        {
            var arguments = new List<string> { "restore", targetPath, "--nologo", "-p:Configuration=" + options.Configuration };
            if (options.Framework is not null) arguments.Add("-p:TargetFramework=" + options.Framework);
            await RunProcessAsync(Path.GetDirectoryName(targetPath)!, arguments, root, cancellationToken);
            await File.WriteAllTextAsync(restored, digest, cancellationToken);
        }
        var requestPath = Path.Combine(directory, "request-" + Guid.NewGuid().ToString("N") + ".json");
        var resultPath = requestPath + ".result";
        try
        {
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new WorkspaceRequest(root, options, analysis.IncludeTests, resultPath)), cancellationToken);
            var assembly = typeof(MSBuildAnalysisProvider).Assembly.Location;
            var bundledWorker = Path.Combine(Path.GetDirectoryName(assembly)!, "msbuild", Path.GetFileName(assembly));
            await RunProcessAsync(Path.GetDirectoryName(targetPath)!, [File.Exists(bundledWorker) ? bundledWorker : assembly, requestPath], root, cancellationToken);
            return JsonSerializer.Deserialize<CallGraph>(await File.ReadAllTextAsync(resultPath, cancellationToken), new JsonSerializerOptions { MaxDepth = 1024 })
                ?? throw new InvalidOperationException("Workspace worker returned no graph.");
        }
        finally
        {
            File.Delete(requestPath);
            File.Delete(resultPath);
        }
    }

    public static AnalysisCoverage WorkspaceCoverage { get; } = new("msbuild", "partial",
        ["possible-dispatch", "unfollowed-accessors-operators-events", "virtual-nonabstract-dispatch", "callbacks-are-possible-calls", "no-framework-dispatch-plugins"]);

    public static string CleanMessage(string message, string root)
    {
        foreach (var prefix in new[] { root, Environment.GetEnvironmentVariable("DOTNET_ROOT"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
            if (!string.IsNullOrEmpty(prefix)) message = message.Replace(prefix, "<workspace>", StringComparison.OrdinalIgnoreCase).Replace(prefix.Replace('\\', '/'), "<workspace>", StringComparison.OrdinalIgnoreCase);
        return message.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static async Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (++attempts < 600) { await Task.Delay(100, cancellationToken); }
        }
    }

    private static async Task RunProcessAsync(string directory, IReadOnlyList<string> arguments, string root, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = directory,
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
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var output = ReadOutputAsync(process.StandardOutput, drain.Token, cancellationToken);
        var error = ReadOutputAsync(process.StandardError, drain.Token, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        drain.CancelAfter(TimeSpan.FromSeconds(2));
        var capturedOutput = await output;
        var capturedError = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("MSBuild analysis failed:\n" + CleanMessage(capturedError + capturedOutput, root).Trim());
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken drainToken, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), drainToken)) != 0)
                output.Append(buffer, 0, count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        return output.ToString();
    }
}
