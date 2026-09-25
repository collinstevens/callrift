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
        var lengths = new int[before.Count + 1, after.Count + 1];
        for (var i = before.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = after.Count - 1; j >= 0; j--)
                lengths[i, j] = Matches(i, j) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        }
        var result = new List<DiffNode>();
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < before.Count || newIndex < after.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (oldIndex < before.Count && newIndex < after.Count && Matches(oldIndex, newIndex))
            {
                var left = before[oldIndex++];
                var right = after[newIndex++];
                if ((left.Label != right.Label || left.ExpandedDispatch != right.ExpandedDispatch)
                    && left.DispatchLabel is not null && left.DispatchLabel == right.DispatchLabel)
                {
                    left = left.ExpandDispatch?.Invoke() ?? left;
                    right = right.ExpandDispatch?.Invoke() ?? right;
                }
                var sameContext = (left.SemanticKey ?? left.Key) == (right.SemanticKey ?? right.Key);
                var contextLimited = sameContext && (left.Omission?.Reason == "generic-context-limit" || right.Omission?.Reason == "generic-context-limit");
                var children = contextLimited ? [] : Compare(left.Children, right.Children, cancellationToken);
                var signatureChanged = left.Side?.SymbolId != right.Side?.SymbolId || left.Signature != right.Signature;
                var contextChanged = !signatureChanged && (left.InvocationKey ?? left.Key) != (right.InvocationKey ?? right.Key);
                var hiddenBodyChange = (left.BodyChanged || right.BodyChanged) && !children.Any(c => c.HasChanges);
                result.Add(new DiffNode(right.Key, right.Label, signatureChanged || contextChanged || hiddenBodyChange ? '~' : ' ', children,
                    signatureChanged ? "signature changed" : contextChanged ? "generic arguments changed" : contextLimited ? "generic context limit" : right.Detail ?? (hiddenBodyChange ? "body changed; visible calls unchanged" : null))
                { Kind = right.Kind, Before = left.Side, After = right.Side, Omission = contextLimited ? new Omission("generic-context-limit") : right.Omission });
            }
            else if (oldIndex < before.Count && (newIndex == after.Count || lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1]))
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
