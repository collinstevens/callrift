namespace Callrift.RealWorldCases;

internal static class RealWorldCaseData
{
    private static readonly IReadOnlyDictionary<string, bool> RepositoryViews = new Dictionary<string, bool>
    {
        ["serilog"] = false,
        ["cleanarchitecture"] = false,
        ["autofac"] = true,
        ["polly"] = true,
        ["ocelot"] = true,
        ["orchardcore"] = true,
        ["aspnetcore"] = true
    };

    private static readonly IReadOnlyDictionary<string, RealWorldCase> Manifest = ReadManifest();

    private static bool Routine => Environment.GetEnvironmentVariable("CALLRIFT_CASE_SET") switch
    {
        null or "" or "all" => false,
        "routine" => true,
        var value => throw new InvalidOperationException($"Unknown case set: {value}. Use all or routine.")
    };

    private static IEnumerable<RealWorldCase> SelectedEntries(string repository) =>
        Manifest.Values.Where(entry => entry.CacheName == repository && (!Routine || entry.Routine));

    public static RealWorldCase Entry(string id) => Manifest[id];

    public static IEnumerable<object[]> Entries(string repository) =>
        SelectedEntries(repository).Where(entry => entry.Views is null).Select(entry => new object[] { entry.Id });

    public static IEnumerable<object[]> Views(string repository) => SelectedEntries(repository)
        .SelectMany(entry => (entry.Views ?? []).Where(view => !Routine || view.Routine).Select(view => new object[] { entry.Id, view.Id }));

    private static IReadOnlyDictionary<string, RealWorldCase> ReadManifest()
    {
        var entries = RealWorldCaseStore.ReadManifest();
        foreach (var entry in entries)
        {
            if (!RepositoryViews.TryGetValue(entry.CacheName, out var reviewedViews))
                throw new InvalidOperationException($"Add a case collection for repository {entry.CacheName} before selecting its cases.");
            if (reviewedViews != (entry.Views is not null))
                throw new InvalidOperationException($"The case collection for {entry.CacheName} does not match the views declared by {entry.Id}.");
        }
        return entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
    }
}
