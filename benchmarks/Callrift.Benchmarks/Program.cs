using BenchmarkDotNet.Running;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Callrift.Benchmarks.EndToEndBenchmarks).Assembly).Run(args).ToArray();
if (args.Any(argument => argument is "--help" or "-h" or "--info" or "--list" || argument.StartsWith("--list=", StringComparison.Ordinal))) return 0;
return summaries.Length > 0 && summaries.All(summary => summary.Reports.Length > 0
    && summary.Reports.All(report => report.Success && report.ResultStatistics is not null)) ? 0 : 1;
