using System.Text;
using Callrift.Cli;

Console.OutputEncoding = new UTF8Encoding(false);
return await CommandRunner.RunAsync(args, Environment.CurrentDirectory, Console.Out, Console.Error);
