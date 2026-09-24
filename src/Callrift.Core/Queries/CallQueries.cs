namespace Callrift.Core;

public sealed record QueryRequest(string Repository, string? Revision = null)
{
    public DiffOptions Options { get; init; } = new();
    public string? Target { get; init; }
    public int MaxPaths { get; init; } = 100;
}

public sealed class CallQueries(IAnalysisProvider? provider = null)
{
    private readonly IAnalysisProvider provider = provider ?? new SourceOnlyAnalysisProvider();

    public async Task<DiffResult> RunAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var repository = await GitRepository.OpenAsync(request.Repository, cancellationToken);
        var revision = request.Revision is null ? null : await repository.ResolveAsync(request.Revision, cancellationToken: cancellationToken);
        var snapshot = await repository.ReadSnapshotAsync(revision, cancellationToken: cancellationToken, allFiles: provider.RequiresProjectFiles);
        var graph = await provider.AnalyzeAsync(snapshot, new AnalysisOptions(request.Options.IncludeTests), cancellationToken);
        return Query(graph, request, cancellationToken) with
        { To = SnapshotIdentities.Create(snapshot, revision is null ? "workingTree" : "revision", request.Revision, revision) };
    }

    public static DiffResult Query(CallGraph graph, QueryRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var roots = EntrySelector.Select(graph, graph, new HashSet<string>(), request.Options);
        var expander = new TreeExpander(graph, new HashSet<string>(), request.Options);
        var trees = roots.Select(expander.Expand).Select(TreeDiffer.Present).ToArray();
        var truncated = trees.Any(CallriftService.IsTruncated);
        if (request.Target is not null)
        {
            var targets = EntrySelector.Select(graph, graph, new HashSet<string>(), new DiffOptions { Entries = [request.Target] }).ToHashSet(StringComparer.Ordinal);
            var paths = new List<DiffNode>();
            void Find(DiffNode node, List<DiffNode> path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (paths.Count > request.MaxPaths) return;
                var next = new List<DiffNode>(path) { node };
                if (targets.Contains(node.Key) || node.After is { TargetIds.Count: 1 } side && targets.Contains(side.TargetIds[0]))
                {
                    DiffNode? chain = null;
                    for (var index = next.Count - 1; index >= 0; index--)
                        chain = next[index] with { Children = chain is null ? [] : [chain] };
                    paths.Add(chain!);
                    return;
                }
                foreach (var child in node.Children) Find(child, next);
            }
            foreach (var tree in trees) Find(tree, []);
            truncated |= paths.Count > request.MaxPaths;
            trees = paths.Take(request.MaxPaths).ToArray();
        }
        return new DiffResult(trees, graph.Diagnostics, false)
        {
            Command = request.Target is null ? "tree" : "reach",
            Coverage = graph.Coverage,
            Truncated = truncated
        };
    }

    private static void Validate(QueryRequest request)
    {
        if (request.Options.Entries.Count == 0 && request.Options.Files.Count == 0) throw new ArgumentException("tree and reach require --entry or --file.");
        if (request.Options.MaxDepth is < 1 or > 100) throw new ArgumentException("Depth must be between 1 and 100.");
        if (request.MaxPaths is < 1 or > 10000) throw new ArgumentException("Max paths must be between 1 and 10000.");
    }
}
