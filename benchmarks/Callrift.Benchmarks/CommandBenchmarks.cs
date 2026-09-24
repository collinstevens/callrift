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

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = CorpusStore.ReadManifest().Single(e => e.Id == "serilog-alignment-guard");
        repository = await CorpusStore.PrepareAsync(entry);
        arguments = ["diff", entry.Before, entry.After, "--entry", "MessageTemplateParser.ParsePropertyToken", "--color", "never"];
    }

    [Benchmark]
    public Task<int> WarmCommand() => CommandRunner.RunAsync(arguments, repository, TextWriter.Null, TextWriter.Null);

    [Benchmark]
    public async Task<int> FreshProcessCommand()
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
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
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
