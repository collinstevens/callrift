using System.Runtime.CompilerServices;
using System.Text.Json;
using Callrift.Core;
using Callrift.MSBuild;

namespace Callrift.FixtureWorker;

internal static class FixtureWorkerHost
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunAsync()
    {
        WorkspaceAnalysis.LoadedWorkspace? loaded = null;
        string? loadedKey = null;
        try
        {
            while (await Console.In.ReadLineAsync() is { } line)
            {
                try
                {
                    var path = JsonSerializer.Deserialize<string>(line) ?? throw new InvalidOperationException("Missing fixture request path.");
                    var request = JsonSerializer.Deserialize<FixtureWorkerRequest>(await File.ReadAllTextAsync(path))
                        ?? throw new InvalidOperationException("Missing fixture request.");
                    var workspace = request.Workspace;
                    CallGraph graph;
                    if (request.PreparedShape is null)
                    {
                        loaded?.Dispose();
                        loaded = null;
                        loadedKey = null;
                        graph = await WorkspaceAnalysis.AnalyzeAsync(workspace);
                    }
                    else
                    {
                        if (loadedKey != request.PreparedShape || loaded?.Matches(workspace) != true)
                        {
                            loaded?.Dispose();
                            loaded = null;
                            loadedKey = null;
                            loaded = await WorkspaceAnalysis.LoadAsync(workspace);
                            loadedKey = request.PreparedShape;
                        }
                        else await loaded.RefreshSourceTextsAsync();
                        graph = await loaded.AnalyzeAsync(workspace.IncludeTests);
                    }
                    await File.WriteAllTextAsync(workspace.ResultPath, JsonSerializer.Serialize(graph, new JsonSerializerOptions { MaxDepth = 1024 }));
                    Console.WriteLine("null");
                }
                catch (Exception error)
                {
                    loaded?.Dispose();
                    loaded = null;
                    loadedKey = null;
                    Console.WriteLine(JsonSerializer.Serialize(error.ToString()));
                }
            }
            return 0;
        }
        finally
        {
            loaded?.Dispose();
        }
    }
}
