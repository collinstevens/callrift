namespace Callrift.Core;

public sealed record CallTree(string Key, string Label, string MatchName, string Signature, IReadOnlyList<CallTree> Children, bool BodyChanged = false, string? Detail = null)
{
    public string Kind { get; init; } = "call";
    public NodeSide? Side { get; init; }
    public Omission? Omission { get; init; }
    internal string? SemanticKey { get; init; }
    internal string? InvocationKey { get; init; }
    internal string? DispatchLabel { get; init; }
    internal bool ExpandedDispatch { get; init; }
    internal Func<CallTree>? ExpandDispatch { get; init; }
}

public sealed class TreeExpander(CallGraph graph, IReadOnlySet<string> changed, DiffOptions options, CancellationToken cancellationToken)
{
    public TreeExpander(CallGraph graph, IReadOnlySet<string> changed, DiffOptions options) : this(graph, changed, options, default) { }

    private readonly CallGraph resolvedGraph = ContextGraph.Create(graph, cancellationToken);
    private readonly Dictionary<string, bool> changeReachability = new(StringComparer.Ordinal);

    public CallTree Expand(string key)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var member = resolvedGraph.Members[key];
        if (!resolvedGraph.Implementations.TryGetValue(key, out var targets) || targets.Count == 0)
        {
            var direct = ExpandMember(key, [], 0);
            return direct with
            {
                DispatchLabel = member.Label,
                ExpandDispatch = () => direct with
                {
                    Children = member.HasBody ? [ExpandMember(key, [], 1) with { Label = "⇢ " + member.Label, Kind = "dispatchTarget" }] : [],
                    BodyChanged = changed.Contains(key),
                    Detail = null,
                    Omission = null,
                    ExpandedDispatch = true
                }
            };
        }
        var call = new CallStep("call", key, member.Label, true, member.Location, []);
        var tree = ExpandCalls([call], [], 0).Single();
        CallTree AsRoot(CallTree value) => value with
        {
            Kind = "member",
            MatchName = member.MatchName,
            Signature = member.Signature,
            BodyChanged = value.BodyChanged || changed.Contains(key),
            Side = value.Side! with { Relation = "definition", CallSites = [] },
            ExpandDispatch = value.ExpandDispatch is { } expand ? () => AsRoot(expand()) : null
        };
        return AsRoot(tree);
    }

    private CallTree ExpandMember(string key, HashSet<string> active, int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var member = resolvedGraph.Members[key];
        var definition = member.DefinitionKey ?? key;
        var side = new NodeSide(definition, member.Signature, "resolved", "direct", [definition], member.Location, [], "definition")
        { ContextTargetKey = key };
        if (member.ContextOmitted)
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], changed.Contains(key), "generic context limit")
            { Kind = "member", Side = side, Omission = new Omission("generic-context-limit") };
        if (active.Contains(key))
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], false, "↺ cycle")
            { Kind = "member", Side = side, Omission = new Omission("cycle", definition) { ReferenceKey = key } };
        if (depth >= options.MaxDepth && HasVisibleCalls(member.Calls))
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], ReachesChange(key), ReachesChange(key) ? "changes below depth limit" : "depth limit")
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
            cancellationToken.ThrowIfCancellationRequested();
            var children = ExpandCalls(call.Children, active, depth + 1);
            var possibleTargets = resolvedGraph.Targets(call, cancellationToken);
            if (call.Kind == "branch")
            {
                if (children.Count > 0)
                    trees.Add(new CallTree(call.Key, call.Label, call.Key, call.Key, children)
                    { Kind = "branch", Side = new NodeSide(null, null, "structural", "none", [], null, [call.Location], call.Relation == "callback" ? "callback" : "branch") { Origin = "structural" } });
                continue;
            }
            if (!call.IsSource && call.Kind != "unresolved" && !options.IncludeExternals && children.Count == 0
                && !resolvedGraph.Members.ContainsKey(call.Key) && possibleTargets.Count == 0)
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
                    tree = new CallTree(call.Key, call.Label, call.Key, call.Key, possibleTargets.Select((t, index) =>
                    {
                        var implementation = ExpandMember(t, active, depth + 1);
                        return implementation with { Key = call.SemanticTargets?[index] ?? t, Label = "⇢ " + implementation.Label, Kind = "dispatchTarget" };
                    }).ToArray());
            }
            else if (resolvedGraph.Members.ContainsKey(call.Key))
                tree = ExpandMember(call.Key, active, depth);
            else
                tree = new CallTree(call.Key, call.Label, call.Key, call.Key, []);
            resolvedGraph.Members.TryGetValue(call.Key, out var declaration);
            var side = new NodeSide(call.Kind == "unresolved" ? null : call.DefinitionKey ?? call.Key, declaration?.Signature,
                call.Kind == "unresolved" ? "unresolved" : "resolved", possibleTargets.Count > 0 ? "possible" : "direct",
                possibleTargets.Count > 0 ? possibleTargets.Select(target => resolvedGraph.Members.GetValueOrDefault(target)?.DefinitionKey ?? target).Distinct(StringComparer.Ordinal).ToArray()
                    : call.Kind == "unresolved" ? [] : [call.DefinitionKey ?? call.Key], declaration?.Location,
                [call.Location], call.Relation, call.Candidates)
            {
                Origin = call.Kind == "unresolved" ? "unknown" : call.IsSource ? "source" : "metadata",
                ContextTargetKey = possibleTargets.Count == 1 ? possibleTargets[0] : null
            };
            tree = tree with
            {
                Kind = "call",
                Side = side,
                Children = tree.Children.Concat(children).ToArray(),
                InvocationKey = InvocationContext.Create(resolvedGraph, call.DefinitionKey ?? call.Key, call.GenericArguments).Identity,
                SemanticKey = (call.SemanticKey ?? call.Key) + (call.SemanticTargets is { Count: > 0 } semanticTargets ? "→" + string.Join(";", semanticTargets) : "")
            };
            if (call.Kind == "call")
                tree = tree with { DispatchLabel = call.Label, ExpandedDispatch = possibleTargets.Count > 1 };
            if (possibleTargets.Count == 1 || possibleTargets.Count == 0 && declaration is not null)
            {
                var compact = tree;
                tree = tree with
                {
                    ExpandDispatch = () =>
                    {
                        var key = possibleTargets.Count == 1 ? possibleTargets[0] : call.Key;
                        var implementation = ExpandMember(key, active, depth + 1);
                        var target = implementation with
                        {
                            Key = possibleTargets.Count == 1 ? call.SemanticTargets?[0] ?? key : call.SemanticKey ?? key,
                            Label = "⇢ " + implementation.Label,
                            Kind = "dispatchTarget"
                        };
                        var implementations = possibleTargets.Count == 1 || declaration!.HasBody ? new[] { target } : [];
                        return compact with
                        {
                            Label = call.Label,
                            MatchName = call.DefinitionKey ?? call.Key,
                            Signature = call.DefinitionKey ?? call.Key,
                            Children = implementations.Concat(children).ToArray(),
                            BodyChanged = false,
                            Detail = null,
                            Omission = null,
                            ExpandDispatch = null,
                            ExpandedDispatch = true
                        };
                    }
                };
            }
            trees.Add(tree);
        }
        return trees;
    }

    private bool HasVisibleCalls(IEnumerable<CallStep> calls)
    {
        foreach (var call in calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (call.Kind != "branch" && (call.IsSource || call.Kind == "unresolved" || options.IncludeExternals
                || resolvedGraph.Members.ContainsKey(call.Key) || resolvedGraph.Targets(call, cancellationToken).Count > 0))
                return true;
            if (HasVisibleCalls(call.Children)) return true;
        }
        return false;
    }

    private bool ReachesChange(string key)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (changed.Count == 0) return false;
        lock (changeReachability)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changeReachability.TryGetValue(key, out var known)) return known;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var result = FindChangedPath(key, visited);
            if (!result)
                foreach (var missing in visited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    changeReachability[missing] = false;
                }
            return result;
        }
    }

    private bool FindChangedPath(string key, HashSet<string> visited)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (changeReachability.TryGetValue(key, out var known)) return known;
        if (changed.Contains(key)) return changeReachability[key] = true;
        if (!visited.Add(key)) return false;
        if (resolvedGraph.Members.TryGetValue(key, out var member))
            foreach (var target in EntrySelector.Targets(member.Calls, resolvedGraph, cancellationToken))
                if (FindChangedPath(target, visited)) return changeReachability[key] = true;
        return false;
    }
}
