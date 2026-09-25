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
                || Fingerprint(oldMember.Calls, before, cancellationToken) != Fingerprint(newMember.Calls, after, cancellationToken))
                changed.Add(key);
        }
        return changed;
    }

    private static bool DefinitionChanged(Member before, Member after) => before.Signature != after.Signature
        || (before.Body is not null || after.Body is not null
            ? !SyntaxFactory.AreEquivalent(before.Body, after.Body, topLevel: false)
            : before.BodyFingerprint != after.BodyFingerprint);

    private static string Fingerprint(IEnumerable<CallStep> calls, CallGraph graph, CancellationToken cancellationToken) => string.Join(";", calls.Select(c =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return c.Kind + ":" + (c.SemanticKey ?? c.Key) + ":" + graph.Members.GetValueOrDefault(c.DefinitionKey ?? c.Key)?.Signature + "[" + string.Join(",", c.SemanticTargets ?? graph.Targets(c, cancellationToken))
            + "]{" + Fingerprint(c.Children, graph, cancellationToken) + "}";
    }));
}
