using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Callrift.Cli;
using Callrift.Corpus;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class CommandBenchmarks
{
    private string repository = "";
    private string[] arguments = [];
    private string revision = "";

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = CorpusStore.ReadManifest().Single(e => e.Id == "serilog-alignment-guard");
        repository = await CorpusStore.PrepareAsync(entry);
        revision = entry.After;
        arguments = ["diff", entry.Before, entry.After, "--entry", "MessageTemplateParser.ParsePropertyToken", "--color", "never"];
    }

    [Benchmark]
    public Task<int> WarmCommand() => InvokeAsync(arguments);

    [Benchmark]
    public Task<int> TreeCommand() => InvokeAsync(["tree", revision, "--entry", "MessageTemplateParser.ParsePropertyToken"]);

    [Benchmark]
    public Task<int> ReachCommand() => InvokeAsync(["reach", revision, "--entry", "MessageTemplateParser.ParsePropertyToken", "--to", "new TextToken"]);

    private async Task<int> InvokeAsync(string[] args)
    {
        using var error = new StringWriter();
        var code = await CommandRunner.RunAsync(args, repository, TextWriter.Null, error);
        if (code != 0) throw new InvalidOperationException(error.ToString());
        return code;
    }

    [Benchmark]
    public Task<int> FreshProcessCommand() => FreshProcessAsync(arguments);

    [Benchmark]
    public Task<int> FreshProcessTreeCommand() => FreshProcessAsync(["tree", revision, "--entry", "MessageTemplateParser.ParsePropertyToken"]);

    [Benchmark]
    public Task<int> FreshProcessReachCommand() => FreshProcessAsync(["reach", revision, "--entry", "MessageTemplateParser.ParsePropertyToken", "--to", "new TextToken"]);

    private async Task<int> FreshProcessAsync(string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(CommandRunner).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        var capturedOutput = await output;
        var capturedError = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(capturedError);
        return capturedOutput.Length + capturedError.Length;
    }
}
