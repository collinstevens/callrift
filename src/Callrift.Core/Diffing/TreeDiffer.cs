namespace Callrift.Core;

public static class TreeDiffer
{
    public static IReadOnlyList<DiffNode> Compare(IReadOnlyList<CallTree> before, IReadOnlyList<CallTree> after) => Compare(before, after, default);

    public static IReadOnlyList<DiffNode> Compare(IReadOnlyList<CallTree> before, IReadOnlyList<CallTree> after, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool Matches(int i, int j)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = before[i];
            var right = after[j];
            if ((left.SemanticKey ?? left.Key) == (right.SemanticKey ?? right.Key)
                && (left.Omission?.Reason == "generic-context-limit" || right.Omission?.Reason == "generic-context-limit")) return true;
            if ((left.Key == right.Key || left.InvocationKey is not null && left.InvocationKey == right.InvocationKey) && (left.Label == right.Label
                || left.DispatchLabel is not null && left.DispatchLabel == right.DispatchLabel)) return true;
            return left.Label == right.Label && left.MatchName == right.MatchName
                && before.Count(n => n.Label == left.Label) == 1 && after.Count(n => n.Label == right.Label) == 1;
        }
        var directions = new byte[before.Count, after.Count];
        var next = new long[after.Count + 1];
        var current = new long[after.Count + 1];
        var matchWeight = (long)Math.Min(before.Count, after.Count) + 1;
        for (var i = before.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = after.Count - 1; j >= 0; j--)
            {
                var match = Matches(i, j) ? next[j + 1] + matchWeight
                    + (before[i].AlignmentKey is { } key && key == after[j].AlignmentKey ? 1 : 0) : -1;
                if (match >= next[j] && match >= current[j + 1])
                {
                    current[j] = match;
                    directions[i, j] = 0;
                }
                else if (next[j] >= current[j + 1])
                {
                    current[j] = next[j];
                    directions[i, j] = 1;
                }
                else
                {
                    current[j] = current[j + 1];
                    directions[i, j] = 2;
                }
            }
            (next, current) = (current, next);
        }
        var result = new List<DiffNode>();
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < before.Count || newIndex < after.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (oldIndex < before.Count && newIndex < after.Count && directions[oldIndex, newIndex] == 0)
            {
                var left = before[oldIndex++];
                var right = after[newIndex++];
                var implementationSignatureChanged = left.Signature != right.Signature && !left.ExpandedDispatch && !right.ExpandedDispatch
                    && (left.Side?.Dispatch == "possible" || right.Side?.Dispatch == "possible");
                if ((left.Label != right.Label || left.ExpandedDispatch != right.ExpandedDispatch || implementationSignatureChanged)
                    && left.DispatchLabel is not null && left.DispatchLabel == right.DispatchLabel)
                {
                    left = left.ExpandDispatch?.Invoke() ?? left;
                    right = right.ExpandDispatch?.Invoke() ?? right;
                }
                var sameContext = (left.SemanticKey ?? left.Key) == (right.SemanticKey ?? right.Key);
                var contextLimited = sameContext && (left.Omission?.Reason == "generic-context-limit" || right.Omission?.Reason == "generic-context-limit");
                var children = contextLimited ? [] : Compare(left.Children, right.Children, cancellationToken);
                var signatureChanged = left.Side?.SymbolId != right.Side?.SymbolId || left.Side?.Signature != right.Side?.Signature || left.Signature != right.Signature;
                var contextChanged = !signatureChanged && (left.InvocationKey ?? left.Key) != (right.InvocationKey ?? right.Key);
                var hiddenBodyChange = (left.BodyChanged || right.BodyChanged) && !children.Any(c => c.HasChanges);
                result.Add(new DiffNode(right.Key, right.Label, signatureChanged || contextChanged || hiddenBodyChange ? '~' : ' ', children,
                    signatureChanged ? "signature changed" : contextChanged ? "generic arguments changed" : contextLimited ? "generic context limit" : right.Detail ?? (hiddenBodyChange ? "body changed; visible calls unchanged" : null))
                { Kind = right.Kind, Before = left.Side, After = right.Side, Omission = contextLimited ? new Omission("generic-context-limit") : right.Omission });
            }
            else if (oldIndex < before.Count && (newIndex == after.Count || directions[oldIndex, newIndex] == 1))
                result.Add(Mark(before[oldIndex++], '-', cancellationToken));
            else
                result.Add(Mark(after[newIndex++], '+', cancellationToken));
        }
        return result;
    }

    public static DiffNode Present(CallTree tree) => Present(tree, default);

    public static DiffNode Present(CallTree tree, CancellationToken cancellationToken) => Mark(tree, ' ', cancellationToken);

    private static DiffNode Mark(CallTree tree, char mark, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(tree.Key, tree.Label, mark, tree.Children.Select(c => Mark(c, mark, cancellationToken)).ToArray(), tree.Detail)
        { Kind = tree.Kind, Before = mark == '-' ? tree.Side : null, After = mark == '-' ? null : tree.Side, Omission = tree.Omission };
    }
}
