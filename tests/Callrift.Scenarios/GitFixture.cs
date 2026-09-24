using System.Diagnostics;
using System.Text;
using Callrift.Cli;
using Callrift.Core;

namespace Callrift.Scenarios;

public sealed class GitFixture : IAsyncDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "callrift-fixture-" + Guid.NewGuid().ToString("N"));
    public string WorkspaceCache { get; } = Path.Combine(Path.GetTempPath(), "callrift-workspace-fixture-" + Guid.NewGuid().ToString("N"));
    public string Before { get; private set; } = "";
    public string After { get; private set; } = "";

    public static async Task<GitFixture> CreateAsync(Scenario scenario)
    {
        var fixture = new GitFixture();
        System.IO.Directory.CreateDirectory(fixture.Directory);
        try
        {
            await fixture.Git("init", "--initial-branch=main");
            await fixture.Git("var", "GIT_AUTHOR_IDENT");
            await fixture.Git("var", "GIT_COMMITTER_IDENT");
            await fixture.Git("config", "--get", "user.signingkey");
            await fixture.Git("config", "--get-regexp", "^(user\\.|commit\\.gpgsign|gpg\\.)");
            await fixture.WriteAsync(scenario.Before);
            await fixture.Git("add", ".");
            await fixture.Git("commit", "-S", "-m", "test: create before fixture");
            fixture.Before = (await fixture.Git("rev-parse", "HEAD")).Trim();
            await fixture.WriteAsync(scenario.After);
            await fixture.Git("add", ".");
            await fixture.Git("commit", "-S", "-m", "test: create after fixture");
            fixture.After = (await fixture.Git("rev-parse", "HEAD")).Trim();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task WriteAsync(IReadOnlyDictionary<string, string> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(Directory, file.Key);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Value.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        }
    }

    public Task<string> Git(params string[] arguments) => GitRepository.RunAsync(Directory, arguments);

    public async Task<string> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(CommandRunner).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["NO_COLOR"] = "1";
        start.Environment["CALLRIFT_WORKSPACE_CACHE"] = WorkspaceCache;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        return $"exit: {process.ExitCode}\nstdout:\n{(await stdout).Replace("\r\n", "\n", StringComparison.Ordinal)}stderr:\n{(await stderr).Replace("\r\n", "\n", StringComparison.Ordinal)}";
    }

    public ValueTask DisposeAsync()
    {
        foreach (var directory in new[] { Directory, WorkspaceCache })
        {
            if (!System.IO.Directory.Exists(directory)) continue;
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(directory, true);
        }
        return ValueTask.CompletedTask;
    }
}
