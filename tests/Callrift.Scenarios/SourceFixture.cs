using Callrift.Core;

namespace Callrift.Scenarios;

public static class SourceFixture
{
    public static async Task<(CallGraph Before, CallGraph After)> AnalyzeAsync(Scenario scenario)
    {
        var provider = new SourceOnlyAnalysisProvider();
        var before = provider.AnalyzeAsync(Snapshot("before", scenario.Before), new AnalysisOptions());
        var after = provider.AnalyzeAsync(Snapshot("after", scenario.After), new AnalysisOptions());
        await Task.WhenAll(before, after);
        return (await before, await after);
    }

    private static SourceSnapshot Snapshot(string name, IReadOnlyDictionary<string, string> files) =>
        new(name, files.Select(file => new SourceFile(file.Key, file.Value, file.Value)).ToArray());
}
