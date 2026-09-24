using System.CommandLine;
using Callrift.Core;

namespace Callrift.Cli;

public static class CommandRunner
{
    public static async Task<int> RunAsync(string[] args, string directory, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        var revisions = new Argument<string[]>("revisions") { Arity = ArgumentArity.ZeroOrMore };
        var entry = new Option<string[]>("--entry", "-e") { AllowMultipleArgumentsPerToken = false };
        var file = new Option<string[]>("--file", "-F") { AllowMultipleArgumentsPerToken = false };
        var depth = new Option<int>("--max-depth", "--depth") { DefaultValueFactory = _ => 6 };
        var context = new Option<string>("--context") { DefaultValueFactory = _ => "2" };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "text" };
        var staged = new Option<bool>("--staged");
        var externals = new Option<bool>("--externals");
        var tests = new Option<bool>("--tests");
        var exitCode = new Option<bool>("--exit-code");
        var strict = new Option<bool>("--strict");
        var diagnostics = new Option<string>("--diagnostics") { DefaultValueFactory = _ => "summary" };
        var from = new Option<string>("--from");
        var to = new Option<string>("--to");
        var color = new Option<string>("--color") { DefaultValueFactory = _ => "auto" };
        var root = new RootCommand("Callrift — semantic call-flow diffs for C#.");
        root.Arguments.Add(revisions);
        foreach (var option in new Option[] { entry, file, depth, context, format, staged, externals, tests, exitCode, strict, from, to, color, diagnostics }) root.Options.Add(option);
        var normalizedArgs = args.FirstOrDefault() == "diff" ? args[1..] : args;
        var separator = Array.IndexOf(normalizedArgs, "--");
        var paths = separator < 0 ? [] : normalizedArgs[(separator + 1)..];
        var parsed = root.Parse(separator < 0 ? normalizedArgs : normalizedArgs[..separator]);
        if (normalizedArgs.Contains("--help") || normalizedArgs.Contains("-h"))
        {
            await output.WriteLineAsync("Usage: callrift [diff] [BEFORE [AFTER]] [options] [-- paths]");
            await output.WriteLineAsync("  --entry/-e NAME   --file/-F PATH   --staged   --from REV --to REV");
            await output.WriteLineAsync("  --max-depth/--depth N (6)   --context N|all (2)   --format text|md");
            await output.WriteLineAsync("  --externals   --tests   --strict   --exit-code   --color auto|always|never");
            await output.WriteLineAsync("  --diagnostics summary|full (summary)");
            return 0;
        }
        if (parsed.Errors.Count > 0)
        {
            foreach (var issue in parsed.Errors) await error.WriteLineAsync(issue.Message);
            return 2;
        }
        try
        {
            var refs = parsed.GetValue(revisions) ?? [];
            var outputFormat = parsed.GetValue(format);
            var colorMode = parsed.GetValue(color);
            if (parsed.GetValue(diagnostics) is not ("summary" or "full")) throw new ArgumentException("Diagnostics must be summary or full.");
            if (outputFormat is not ("text" or "md" or "markdown")) throw new ArgumentException("M1 supports --format text|md.");
            if (colorMode is not ("auto" or "always" or "never")) throw new ArgumentException("Color must be auto, always, or never.");
            if (refs.Length > 2) throw new ArgumentException("Expected at most two revisions; separate paths with --.");
            if (refs.Length > 0 && (parsed.GetValue(from) is not null || parsed.GetValue(to) is not null)) throw new ArgumentException("Do not mix positional revisions with --from/--to.");
            if (parsed.GetValue(staged) && (refs.Length > 1 || parsed.GetValue(to) is not null)) throw new ArgumentException("--staged accepts only a baseline revision.");
            var contextText = parsed.GetValue(context);
            var contextCount = contextText == "all" ? -1 : int.TryParse(contextText, out var count) && count >= 0 ? count : throw new ArgumentException("Context must be non-negative or 'all'.");
            var options = new DiffOptions
            {
                Entries = parsed.GetValue(entry) ?? [],
                Files = parsed.GetValue(file) ?? [],
                Paths = paths,
                MaxDepth = parsed.GetValue(depth),
                Context = contextCount,
                IncludeExternals = parsed.GetValue(externals),
                IncludeTests = parsed.GetValue(tests)
            };
            var request = new DiffRequest(directory, refs.FirstOrDefault() ?? parsed.GetValue(from), refs.ElementAtOrDefault(1) ?? parsed.GetValue(to), parsed.GetValue(staged)) { Options = options };
            var result = await new CallriftService().DiffAsync(request, cancellationToken);
            var diagnosticLimit = parsed.GetValue(diagnostics) == "full" ? int.MaxValue : 8;
            foreach (var diagnostic in result.Diagnostics.Take(diagnosticLimit))
            {
                var location = diagnostic.Location is null ? "" : $"{diagnostic.Location.Path}:{diagnostic.Location.Line}: ";
                await error.WriteLineAsync($"{location}{diagnostic.Code}: {diagnostic.Message}");
            }
            if (result.Diagnostics.Count > diagnosticLimit)
                await error.WriteLineAsync($"{result.Diagnostics.Count - diagnosticLimit} additional diagnostics; use --diagnostics full.");
            var rendered = DiffRenderer.Render(result, options, outputFormat != "text");
            var useColor = outputFormat == "text" && (colorMode == "always" || colorMode == "auto" && ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null);
            if (useColor)
                rendered = string.Join('\n', rendered.Split('\n').Select(line => line.Length == 0 ? line : line[0] switch
                {
                    '+' => "\u001b[32m" + line + "\u001b[0m",
                    '-' => "\u001b[31m" + line + "\u001b[0m",
                    '~' => "\u001b[33m" + line + "\u001b[0m",
                    _ => line
                }));
            await output.WriteAsync(rendered);
            return parsed.GetValue(strict) && result.Diagnostics.Count > 0 ? 2 : parsed.GetValue(exitCode) && result.HasChanges ? 1 : 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or OperationCanceledException)
        {
            await error.WriteLineAsync("callrift: " + exception.Message);
            return 2;
        }
    }
}
