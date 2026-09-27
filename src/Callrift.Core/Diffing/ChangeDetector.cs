using Microsoft.CodeAnalysis.CSharp;

namespace Callrift.Core;

public static class ChangeDetector
{
    public static HashSet<string> FindChanges(CallGraph before, CallGraph after) => FindChanges(before, after, default);

    public static HashSet<string> FindChanges(CallGraph before, CallGraph after, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shared = ReferenceEquals(before, after);
        before = ContextGraph.Create(before, cancellationToken);
        after = shared ? before : ContextGraph.Create(after, cancellationToken);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in before.Members.Keys.Union(after.Members.Keys, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            before.Members.TryGetValue(key, out var oldMember);
            after.Members.TryGetValue(key, out var newMember);
            if (oldMember is null || newMember is null)
            {
                var present = oldMember ?? newMember!;
                var other = (oldMember is null ? before : after).Members.GetValueOrDefault(present.DefinitionKey ?? key);
                if (other is null || DefinitionChanged(present, other)) changed.Add(key);
                continue;
            }
            if (DefinitionChanged(oldMember, newMember)
                || !CallsEqual(oldMember.Calls, newMember.Calls, before, after, cancellationToken))
                changed.Add(key);
        }
        return changed;
    }

    private static bool DefinitionChanged(Member before, Member after) => before.Signature != after.Signature
        || (before.Body is not null || after.Body is not null
            ? !SyntaxFactory.AreEquivalent(before.Body, after.Body, topLevel: false)
            : before.BodyFingerprint != after.BodyFingerprint);

    private static bool CallsEqual(IReadOnlyList<CallStep> before, IReadOnlyList<CallStep> after, CallGraph beforeGraph, CallGraph afterGraph,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (before.Count != after.Count) return false;
        for (var index = 0; index < before.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = before[index];
            var right = after[index];
            if (left.Kind != right.Kind || (left.SemanticKey ?? left.Key) != (right.SemanticKey ?? right.Key)
                || beforeGraph.Members.GetValueOrDefault(left.DefinitionKey ?? left.Key)?.Signature != afterGraph.Members.GetValueOrDefault(right.DefinitionKey ?? right.Key)?.Signature
                || !(left.SemanticTargets ?? beforeGraph.Targets(left, cancellationToken)).SequenceEqual(right.SemanticTargets ?? afterGraph.Targets(right, cancellationToken), StringComparer.Ordinal)
                || !CallsEqual(left.Children, right.Children, beforeGraph, afterGraph, cancellationToken)) return false;
        }
        return true;
    }
}
