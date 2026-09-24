namespace Callrift.Core;

public sealed class CallriftService(IAnalysisProvider? provider = null)
{
    private readonly IAnalysisProvider provider = provider ?? new SourceOnlyAnalysisProvider();

    public async Task<DiffResult> DiffAsync(DiffRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Options.MaxDepth < 1 || request.Options.MaxDepth > 100)
            throw new ArgumentException("Depth must be between 1 and 100.");
        var repository = await GitRepository.OpenAsync(request.Repository, cancellationToken);
        string? beforeId;
        string? afterId;
        if (request.Before?.Contains("...", StringComparison.Ordinal) == true)
        {
            if (request.After is not null || request.Staged) throw new ArgumentException("Merge-base syntax cannot be combined with a second revision or --staged.");
            var parts = request.Before.Split("...", StringSplitOptions.None);
            if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty)) throw new ArgumentException("Expected LEFT...RIGHT.");
            var left = await repository.ResolveAsync(parts[0], cancellationToken: cancellationToken);
            afterId = await repository.ResolveAsync(parts[1], cancellationToken: cancellationToken);
            beforeId = await repository.MergeBaseAsync(left!, afterId!, cancellationToken);
        }
        else
        {
            beforeId = await repository.ResolveAsync(request.Before ?? "HEAD", request.Before is null, cancellationToken);
            afterId = request.After is null ? null : await repository.ResolveAsync(request.After, cancellationToken: cancellationToken);
        }
        var beforeRead = beforeId is null ? Task.FromResult(new SourceSnapshot("empty", [])) : repository.ReadSnapshotAsync(beforeId, cancellationToken: cancellationToken, allFiles: provider.RequiresProjectFiles);
        var afterRead = repository.ReadSnapshotAsync(afterId, request.Staged, cancellationToken, provider.RequiresProjectFiles);
        await Task.WhenAll(beforeRead, afterRead);
        var before = await beforeRead;
        var after = await afterRead;
        return (await DiffAsync(before, after, request.Options, cancellationToken)) with
        {
            From = SnapshotIdentities.Create(before, beforeId is null ? "empty" : "revision", request.Before ?? "HEAD", beforeId),
            To = SnapshotIdentities.Create(after, afterId is null ? request.Staged ? "index" : "workingTree" : "revision", request.After ?? (request.Before?.Contains("...", StringComparison.Ordinal) == true ? request.Before.Split("...")[1] : null), afterId)
        };
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
        return new DiffResult(trees, before.Diagnostics.Concat(after.Diagnostics).Distinct().ToArray(), trees.Length > 0)
        {
            Truncated = trees.Any(IsTruncated),
            Coverage = new AnalysisCoverage(after.Coverage.Mode,
                before.Coverage.Status == "complete" && after.Coverage.Status == "complete" ? "complete" : "partial",
                before.Coverage.Limitations.Concat(after.Coverage.Limitations).Distinct(StringComparer.Ordinal).ToArray())
        };
    }

    internal static bool IsTruncated(DiffNode node) => node.Omission?.Reason == "depth-limit" || node.Children.Any(IsTruncated);
}
