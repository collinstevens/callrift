using System.Text;
using System.Text.Json;
using Callrift.MSBuild;
using Microsoft.Build.Locator;

Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length != 1) return 2;
var request = JsonSerializer.Deserialize<WorkspaceRequest>(await File.ReadAllTextAsync(args[0]))!;
try
{
    MSBuildLocator.RegisterDefaults();
    var graph = await WorkspaceAnalysis.AnalyzeAsync(request);
    await File.WriteAllTextAsync(request.ResultPath, JsonSerializer.Serialize(graph, new JsonSerializerOptions { MaxDepth = 1024 }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(MSBuildAnalysisProvider.CleanMessage(error.Message, request.Root));
    return 2;
}
