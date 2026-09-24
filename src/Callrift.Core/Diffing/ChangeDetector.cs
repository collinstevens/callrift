using Microsoft.CodeAnalysis.CSharp;

namespace Callrift.Core;

public static class ChangeDetector
{
    public static HashSet<string> FindChanges(CallGraph before, CallGraph after)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in before.Members.Keys.Union(after.Members.Keys, StringComparer.Ordinal))
        {
            if (!before.Members.TryGetValue(key, out var oldMember) || !after.Members.TryGetValue(key, out var newMember)
                || oldMember.Signature != newMember.Signature
                || !SyntaxFactory.AreEquivalent(oldMember.Body, newMember.Body, topLevel: false)
                || Fingerprint(oldMember.Calls, before) != Fingerprint(newMember.Calls, after))
                changed.Add(key);
        }
        return changed;
    }

    private static string Fingerprint(IEnumerable<CallStep> calls, CallGraph graph) => string.Join(";", calls.Select(c =>
        c.Kind + ":" + c.Key + "[" + (graph.Implementations.TryGetValue(c.Key, out var targets) ? string.Join(",", targets) : "")
        + "]{" + Fingerprint(c.Children, graph) + "}"));
}
