namespace Callrift.Core;

public static class TreeDiffer
{
    public static IReadOnlyList<DiffNode> Compare(IReadOnlyList<CallTree> before, IReadOnlyList<CallTree> after)
    {
        bool Matches(int i, int j)
        {
            var left = before[i];
            var right = after[j];
            if (left.Key == right.Key && left.Label == right.Label) return true;
            return left.Label == right.Label && left.MatchName == right.MatchName
                && before.Count(n => n.Label == left.Label) == 1 && after.Count(n => n.Label == right.Label) == 1;
        }
        var lengths = new int[before.Count + 1, after.Count + 1];
        for (var i = before.Count - 1; i >= 0; i--)
            for (var j = after.Count - 1; j >= 0; j--)
                lengths[i, j] = Matches(i, j) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var result = new List<DiffNode>();
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < before.Count || newIndex < after.Count)
        {
            if (oldIndex < before.Count && newIndex < after.Count && Matches(oldIndex, newIndex))
            {
                var left = before[oldIndex++];
                var right = after[newIndex++];
                var children = Compare(left.Children, right.Children);
                var signatureChanged = left.Key != right.Key || left.Signature != right.Signature;
                var hiddenBodyChange = (left.BodyChanged || right.BodyChanged) && !children.Any(c => c.HasChanges);
                result.Add(new DiffNode(right.Key, right.Label, signatureChanged || hiddenBodyChange ? '~' : ' ', children,
                    signatureChanged ? "signature changed" : right.Detail ?? (hiddenBodyChange ? "body changed; visible calls unchanged" : null)));
            }
            else if (oldIndex < before.Count && (newIndex == after.Count || lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1]))
                result.Add(Mark(before[oldIndex++], '-'));
            else
                result.Add(Mark(after[newIndex++], '+'));
        }
        return result;
    }

    private static DiffNode Mark(CallTree tree, char mark) => new(tree.Key, tree.Label, mark, tree.Children.Select(c => Mark(c, mark)).ToArray(), tree.Detail);
}
