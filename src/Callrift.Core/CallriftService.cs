namespace Callrift.Core;

public sealed class CallriftService(IAnalysisProvider? provider = null)
{
    private readonly IAnalysisProvider provider = provider ?? new SourceOnlyAnalysisProvider();

    public async Task<DiffResult> DiffAsync(DiffRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Options.MaxDepth < 1 || request.Options.MaxDepth > 100)
            throw new ArgumentException("Depth must be between 1 and 100.");
        var repository = await GitRepository.OpenAsync(request.Repository, cancellationToken);
        var beforeId = await repository.ResolveAsync(request.Before ?? "HEAD", request.Before is null, cancellationToken);
        var afterId = request.After is null ? null : await repository.ResolveAsync(request.After, cancellationToken: cancellationToken);
        var beforeRead = beforeId is null ? Task.FromResult(new SourceSnapshot("empty", [])) : repository.ReadSnapshotAsync(beforeId, cancellationToken: cancellationToken);
        var afterRead = repository.ReadSnapshotAsync(afterId, request.Staged, cancellationToken);
        await Task.WhenAll(beforeRead, afterRead);
        return await DiffAsync(await beforeRead, await afterRead, request.Options, cancellationToken);
    }

    public async Task<DiffResult> DiffAsync(SourceSnapshot before, SourceSnapshot after, DiffOptions options, CancellationToken cancellationToken = default)
    {
        var oldTask = provider.AnalyzeAsync(before, new AnalysisOptions(options.IncludeTests), cancellationToken);
        var newTask = provider.AnalyzeAsync(after, new AnalysisOptions(options.IncludeTests), cancellationToken);
        await Task.WhenAll(oldTask, newTask);
        return Compare(await oldTask, await newTask, options);
    }

    public static DiffResult Compare(CallGraph before, CallGraph after, DiffOptions options)
    {
        var changed = ChangeDetector.FindChanges(before, after);
        var roots = EntrySelector.Select(before, after, changed, options);
        var oldExpander = new TreeExpander(before, changed, options);
        var newExpander = new TreeExpander(after, changed, options);
        var oldTrees = roots.Where(before.Members.ContainsKey).Select(oldExpander.Expand).OrderBy(t => t.MatchName, StringComparer.Ordinal).ThenBy(t => t.Key, StringComparer.Ordinal).ToArray();
        var newTrees = roots.Where(after.Members.ContainsKey).Select(newExpander.Expand).OrderBy(t => t.MatchName, StringComparer.Ordinal).ThenBy(t => t.Key, StringComparer.Ordinal).ToArray();
        var trees = TreeDiffer.Compare(oldTrees, newTrees).Where(t => t.HasChanges).ToArray();
        return new DiffResult(trees, before.Diagnostics.Concat(after.Diagnostics).Distinct().ToArray(), trees.Length > 0);
    }
}
