using System.Text;
using Callrift.Cli;

Console.OutputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
return await CommandRunner.RunAsync(args, Environment.CurrentDirectory, Console.Out, Console.Error, cancellation.Token);
