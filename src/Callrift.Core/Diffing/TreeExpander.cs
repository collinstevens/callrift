namespace Callrift.Core;

public sealed record CallTree(string Key, string Label, string MatchName, string Signature, IReadOnlyList<CallTree> Children, bool BodyChanged = false, string? Detail = null)
{
    public string Kind { get; init; } = "call";
    public NodeSide? Side { get; init; }
    public Omission? Omission { get; init; }
}

public sealed class TreeExpander(CallGraph graph, IReadOnlySet<string> changed, DiffOptions options)
{
    public CallTree Expand(string key)
    {
        if (!graph.Implementations.TryGetValue(key, out var targets) || targets.Count == 0)
            return ExpandMember(key, [], 0);
        var member = graph.Members[key];
        var call = new CallStep("call", key, member.Label, true, member.Location, []);
        var tree = ExpandCalls([call], [], 0).Single();
        return tree with
        {
            Kind = "member",
            MatchName = member.MatchName,
            Signature = member.Signature,
            BodyChanged = tree.BodyChanged || changed.Contains(key),
            Side = tree.Side! with { Relation = "definition", CallSites = [] }
        };
    }

    private CallTree ExpandMember(string key, HashSet<string> active, int depth)
    {
        var member = graph.Members[key];
        var side = new NodeSide(key, member.Signature, "resolved", "direct", [key], member.Location, [], "definition");
        if (active.Contains(key))
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], false, "↺ cycle")
            { Kind = "member", Side = side, Omission = new Omission("cycle", key) };
        if (depth >= options.MaxDepth)
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], ReachesChange(key, []), ReachesChange(key, []) ? "changes below depth limit" : "depth limit")
            { Kind = "member", Side = side, Omission = new Omission("depth-limit") };
        var path = new HashSet<string>(active, StringComparer.Ordinal) { key };
        return new CallTree(key, member.Label, member.MatchName, member.Signature, ExpandCalls(member.Calls, path, depth + 1), changed.Contains(key))
        { Kind = "member", Side = side };
    }

    private IReadOnlyList<CallTree> ExpandCalls(IEnumerable<CallStep> calls, HashSet<string> active, int depth)
    {
        var trees = new List<CallTree>();
        foreach (var call in calls)
        {
            var children = ExpandCalls(call.Children, active, depth + 1);
            var possibleTargets = graph.Targets(call);
            if (call.Kind == "branch")
            {
                if (children.Count > 0)
                    trees.Add(new CallTree(call.Key, call.Label, call.Key, call.Key, children)
                    { Kind = "branch", Side = new NodeSide(null, null, "structural", "none", [], null, [call.Location], call.Relation == "callback" ? "callback" : "branch") { Origin = "structural" } });
                continue;
            }
            if (!call.IsSource && call.Kind != "unresolved" && !options.IncludeExternals && children.Count == 0
                && !graph.Members.ContainsKey(call.Key) && possibleTargets.Count == 0)
                continue;
            CallTree tree;
            if (possibleTargets.Count > 0)
            {
                if (possibleTargets.Count == 1)
                {
                    var implementation = ExpandMember(possibleTargets[0], active, depth);
                    tree = implementation with { Key = call.Key, Label = call.Label + " → " + implementation.Label, MatchName = call.Label + " → " + implementation.MatchName };
                }
                else
                    tree = new CallTree(call.Key, call.Label, call.Key, call.Key, possibleTargets.Select(t =>
                    {
                        var implementation = ExpandMember(t, active, depth + 1);
                        return implementation with { Label = "⇢ " + implementation.Label, Kind = "dispatchTarget" };
                    }).ToArray());
            }
            else if (graph.Members.ContainsKey(call.Key))
                tree = ExpandMember(call.Key, active, depth);
            else
                tree = new CallTree(call.Key, call.Label, call.Key, call.Key, []);
            graph.Members.TryGetValue(call.Key, out var declaration);
            var side = new NodeSide(call.Kind == "unresolved" ? null : call.Key, declaration?.Signature,
                call.Kind == "unresolved" ? "unresolved" : "resolved", possibleTargets.Count > 0 ? "possible" : "direct",
                possibleTargets.Count > 0 ? possibleTargets : call.Kind == "unresolved" ? [] : [call.Key], declaration?.Location,
                [call.Location], call.Relation, call.Candidates)
            { Origin = call.Kind == "unresolved" ? "unknown" : call.IsSource ? "source" : "metadata" };
            trees.Add(tree with { Kind = "call", Side = side, Children = tree.Children.Concat(children).ToArray() });
        }
        return trees;
    }

    private bool ReachesChange(string key, HashSet<string> visited)
    {
        if (!visited.Add(key)) return false;
        if (changed.Contains(key)) return true;
        return graph.Members.TryGetValue(key, out var member) && EntrySelector.Targets(member.Calls, graph).Any(t => ReachesChange(t, visited));
    }
}
