using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Callrift.MSBuild;
using Microsoft.Build.Locator;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length != 1) return 2;
using var parent = Process.GetProcessById(int.Parse(args[0], CultureInfo.InvariantCulture));
parent.EnableRaisingEvents = true;
parent.Exited += (_, _) => Process.GetCurrentProcess().Kill(true);
if (parent.HasExited) return 2;
MSBuildLocator.RegisterDefaults();
while (await Console.In.ReadLineAsync() is { } line)
{
    try
    {
        var path = JsonSerializer.Deserialize<string>(line) ?? throw new InvalidOperationException("Missing fixture request path.");
        var request = JsonSerializer.Deserialize<WorkspaceRequest>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("Missing fixture request.");
        var graph = await WorkspaceAnalysis.AnalyzeAsync(request);
        await File.WriteAllTextAsync(request.ResultPath, JsonSerializer.Serialize(graph, new JsonSerializerOptions { MaxDepth = 1024 }));
        Console.WriteLine("null");
    }
    catch (Exception error)
    {
        Console.WriteLine(JsonSerializer.Serialize(error.ToString()));
    }
}
return 0;
