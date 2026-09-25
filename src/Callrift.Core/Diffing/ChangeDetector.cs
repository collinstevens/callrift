using Microsoft.CodeAnalysis.CSharp;

namespace Callrift.Core;

public static class ChangeDetector
{
    public static HashSet<string> FindChanges(CallGraph before, CallGraph after) => FindChanges(before, after, default);

    public static HashSet<string> FindChanges(CallGraph before, CallGraph after, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in before.Members.Keys.Union(after.Members.Keys, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!before.Members.TryGetValue(key, out var oldMember) || !after.Members.TryGetValue(key, out var newMember)
                || oldMember.Signature != newMember.Signature
                || (oldMember.Body is not null || newMember.Body is not null
                    ? !SyntaxFactory.AreEquivalent(oldMember.Body, newMember.Body, topLevel: false)
                    : oldMember.BodyFingerprint != newMember.BodyFingerprint)
                || Fingerprint(oldMember.Calls, before, cancellationToken) != Fingerprint(newMember.Calls, after, cancellationToken))
                changed.Add(key);
        }
        return changed;
    }

    private static string Fingerprint(IEnumerable<CallStep> calls, CallGraph graph, CancellationToken cancellationToken) => string.Join(";", calls.Select(c =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return c.Kind + ":" + c.Key + "[" + string.Join(",", graph.Targets(c, cancellationToken))
            + "]{" + Fingerprint(c.Children, graph, cancellationToken) + "}";
    }));
}
