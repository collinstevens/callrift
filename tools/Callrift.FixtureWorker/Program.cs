using System.Diagnostics;
using System.Globalization;
using System.Text;
using Callrift.FixtureWorker;
using Microsoft.Build.Locator;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length != 1) return 2;
using var parent = Process.GetProcessById(int.Parse(args[0], CultureInfo.InvariantCulture));
parent.EnableRaisingEvents = true;
parent.Exited += (_, _) => Process.GetCurrentProcess().Kill(true);
if (parent.HasExited) return 2;
MSBuildLocator.RegisterDefaults();
return await FixtureWorkerHost.RunAsync();
