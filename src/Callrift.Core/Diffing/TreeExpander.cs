namespace Callrift.Core;

public sealed record CallTree(string Key, string Label, string MatchName, string Signature, IReadOnlyList<CallTree> Children, bool BodyChanged = false, string? Detail = null);

public sealed class TreeExpander(CallGraph graph, IReadOnlySet<string> changed, DiffOptions options)
{
    public CallTree Expand(string key) => ExpandMember(key, [], 0);

    private CallTree ExpandMember(string key, HashSet<string> active, int depth)
    {
        var member = graph.Members[key];
        if (active.Contains(key))
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], false, "↺ cycle");
        if (depth >= options.MaxDepth)
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], ReachesChange(key, []), ReachesChange(key, []) ? "changes below depth limit" : "depth limit");
        var path = new HashSet<string>(active, StringComparer.Ordinal) { key };
        return new CallTree(key, member.Label, member.MatchName, member.Signature, ExpandCalls(member.Calls, path, depth + 1), changed.Contains(key));
    }

    private IReadOnlyList<CallTree> ExpandCalls(IEnumerable<CallStep> calls, HashSet<string> active, int depth)
    {
        var trees = new List<CallTree>();
        foreach (var call in calls)
        {
            var children = ExpandCalls(call.Children, active, depth + 1);
            if (call.Kind == "branch")
            {
                if (children.Count > 0)
                    trees.Add(new CallTree(call.Key, call.Label, call.Key, call.Key, children));
                continue;
            }
            if (!call.IsSource && call.Kind != "unresolved" && !options.IncludeExternals && children.Count == 0)
                continue;
            CallTree tree;
            if (graph.Implementations.TryGetValue(call.Key, out var targets) && targets.Count > 0)
            {
                if (targets.Count == 1)
                {
                    var implementation = ExpandMember(targets[0], active, depth);
                    tree = implementation with { Key = call.Key, Label = call.Label + " → " + implementation.Label, MatchName = call.Label + " → " + implementation.MatchName };
                }
                else
                    tree = new CallTree(call.Key, call.Label, call.Key, call.Key, targets.Select(t =>
                    {
                        var implementation = ExpandMember(t, active, depth + 1);
                        return implementation with { Label = "⇢ " + implementation.Label };
                    }).ToArray());
            }
            else if (graph.Members.ContainsKey(call.Key))
                tree = ExpandMember(call.Key, active, depth);
            else
                tree = new CallTree(call.Key, call.Label, call.Key, call.Key, []);
            trees.Add(tree with { Children = tree.Children.Concat(children).ToArray() });
        }
        return trees;
    }

    private bool ReachesChange(string key, HashSet<string> visited)
    {
        if (!visited.Add(key)) return false;
        if (changed.Contains(key)) return true;
        if (graph.Implementations.TryGetValue(key, out var implementations) && implementations.Any(t => ReachesChange(t, visited))) return true;
        return graph.Members.TryGetValue(key, out var member) && EntrySelector.Targets(member.Calls).Any(t => ReachesChange(t, visited));
    }
}
