namespace Callrift.Core;

public sealed record CallTree(string Key, string Label, string MatchName, string Signature, IReadOnlyList<CallTree> Children, bool BodyChanged = false, string? Detail = null)
{
    public string Kind { get; init; } = "call";
    public NodeSide? Side { get; init; }
    public Omission? Omission { get; init; }
    internal string? SemanticKey { get; init; }
    internal string? InvocationKey { get; init; }
    internal string? AlignmentKey { get; init; }
    internal string? DispatchLabel { get; init; }
    internal bool ExpandedDispatch { get; init; }
    internal Func<CallTree>? ExpandDispatch { get; init; }
}

public sealed class TreeExpander(CallGraph graph, IReadOnlySet<string> changed, DiffOptions options, CancellationToken cancellationToken)
{
    public TreeExpander(CallGraph graph, IReadOnlySet<string> changed, DiffOptions options) : this(graph, changed, options, default) { }

    private readonly CallGraph resolvedGraph = ContextGraph.Create(graph, cancellationToken);
    private readonly Lock changeReachabilityLock = new();
    private HashSet<string>? changeReachability;
    private HashSet<string>? changesWithoutInitialization;

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
                    Signature = key,
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
            BodyChanged = value.BodyChanged || changed.Contains(key),
            Side = value.Side! with { Relation = "definition", CallSites = [] },
            ExpandDispatch = value.ExpandDispatch is { } expand ? () => AsRoot(expand()) : null
        };
        return AsRoot(tree);
    }

    private CallTree ExpandMember(string key, HashSet<string> active, int depth, HashSet<string>? initialized = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        initialized ??= new HashSet<string>(StringComparer.Ordinal);
        var member = resolvedGraph.Members[key];
        if (member.TypeInitializerType is not null) initialized.Add(key);
        var definition = member.DefinitionKey ?? key;
        var side = new NodeSide(definition, member.Signature, "resolved", "direct", [definition], member.Location, [], "definition")
        { ContextTargetKey = key };
        if (member.ContextOmitted)
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], changed.Contains(key), "generic context limit")
            { Kind = "member", Side = side, Omission = new Omission("generic-context-limit") };
        if (active.Contains(key))
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], false, "↺ cycle")
            { Kind = "member", Side = side, Omission = new Omission("cycle", definition) { ReferenceKey = key } };
        if (depth >= options.MaxDepth && HasVisibleCalls(member.Calls, initialized))
        {
            var reachesChange = ReachesChange(key, initialized);
            return new CallTree(key, member.Label, member.MatchName, member.Signature, [], reachesChange, reachesChange ? "changes below depth limit" : "depth limit")
            { Kind = "member", Side = side, Omission = new Omission("depth-limit") };
        }
        var prelude = member.Calls.TakeWhile(call => call.IsInitialization).ToArray();
        var initializers = ExpandCalls(prelude, active, depth + 1, initialized);
        var path = new HashSet<string>(active, StringComparer.Ordinal) { key };
        var body = ExpandCalls(member.Calls.Skip(prelude.Length), path, depth + 1, initialized);
        return new CallTree(key, member.Label, member.MatchName, member.Signature, initializers.Concat(body).ToArray(), changed.Contains(key))
        { Kind = "member", Side = side };
    }

    private IReadOnlyList<CallTree> ExpandCalls(IEnumerable<CallStep> calls, HashSet<string> active, int depth, HashSet<string>? initialized = null)
    {
        if (calls is IReadOnlyCollection<CallStep> { Count: 0 }) return [];
        initialized ??= new HashSet<string>(StringComparer.Ordinal);
        var trees = new List<CallTree>();
        Dictionary<int, HashSet<string>>? callbackInitializations = null;
        HashSet<string>? completedParts = null;
        foreach (var call in calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentInitializations = initialized;
            if (call.CallbackGroup is { } group)
            {
                callbackInitializations ??= new Dictionary<int, HashSet<string>>();
                if (!callbackInitializations.TryGetValue(group, out var callbackState))
                    callbackInitializations[group] = callbackState = new HashSet<string>(currentInitializations, StringComparer.Ordinal);
                currentInitializations = callbackState;
            }
            if (!call.IsInitializationPart && completedParts is not null)
            {
                initialized.UnionWith(completedParts);
                completedParts = null;
            }
            if (call.IsInitialization && call.Children.FirstOrDefault() is { } initializer && !currentInitializations.Add(initializer.Key)) continue;
            var possibleTargets = resolvedGraph.Targets(call, cancellationToken);
            resolvedGraph.Members.TryGetValue(call.Key, out var declaration);
            var canExpandDispatch = call.Kind != "branch" && (possibleTargets.Count == 1 || possibleTargets.Count == 0 && declaration is not null);
            var invocationInitializations = canExpandDispatch ? new HashSet<string>(currentInitializations, StringComparer.Ordinal) : null;
            var childInitializations = call.IsInitialization || call.Children.Count == 0 ? currentInitializations : new HashSet<string>(currentInitializations, StringComparer.Ordinal);
            var children = call.Children.Count == 0 ? [] : ExpandCalls(call.Children, active, depth + 1, childInitializations);
            if (call.IsInitializationPart)
            {
                completedParts ??= new HashSet<string>(StringComparer.Ordinal);
                completedParts.UnionWith(childInitializations);
            }
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
                    var implementation = ExpandMember(possibleTargets[0], active, depth, new HashSet<string>(currentInitializations, StringComparer.Ordinal));
                    tree = implementation with { Key = call.Key, Label = call.Label + " → " + implementation.Label, MatchName = call.Label + " → " + implementation.MatchName };
                }
                else
                    tree = new CallTree(call.Key, call.Label, call.Key, call.Key, possibleTargets.Select((t, index) =>
                    {
                        var implementation = ExpandMember(t, active, depth + 1, new HashSet<string>(currentInitializations, StringComparer.Ordinal));
                        return implementation with { Key = call.SemanticTargets?[index] ?? t, Label = "⇢ " + implementation.Label, Kind = "dispatchTarget" };
                    }).ToArray());
            }
            else if (resolvedGraph.Members.ContainsKey(call.Key))
                tree = ExpandMember(call.Key, active, depth, currentInitializations);
            else
                tree = new CallTree(call.Key, call.Label, call.Key, call.Key, []);
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
                DispatchLabel = call.Kind == "call" ? call.Label : tree.DispatchLabel,
                ExpandedDispatch = call.Kind == "call" ? possibleTargets.Count > 1 : tree.ExpandedDispatch,
                AlignmentKey = call.AlignmentKey,
                Side = side,
                Children = children.Count == 0 ? tree.Children : tree.Children.Concat(children).ToArray(),
                InvocationKey = InvocationContext.Create(resolvedGraph, call.DefinitionKey ?? call.Key, call.GenericArguments).Identity,
                SemanticKey = (call.SemanticKey ?? call.Key) + (call.SemanticTargets is { Count: > 0 } semanticTargets ? "→" + string.Join(";", semanticTargets) : "")
            };
            if (canExpandDispatch)
            {
                var compact = tree;
                tree = tree with
                {
                    ExpandDispatch = () =>
                    {
                        var key = possibleTargets.Count == 1 ? possibleTargets[0] : call.Key;
                        var implementation = ExpandMember(key, active, depth + 1, new HashSet<string>(invocationInitializations!, StringComparer.Ordinal));
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
        if (completedParts is not null) initialized.UnionWith(completedParts);
        return trees;
    }

    private bool HasVisibleCalls(IEnumerable<CallStep> calls, IReadOnlySet<string> initialized)
    {
        foreach (var call in calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (call.IsInitialization && call.Children.FirstOrDefault() is { } initializer && initialized.Contains(initializer.Key)) continue;
            if (call.Kind != "branch" && (call.IsSource || call.Kind == "unresolved" || options.IncludeExternals
                || resolvedGraph.Members.ContainsKey(call.Key) || resolvedGraph.Targets(call, cancellationToken).Count > 0))
                return true;
            if (HasVisibleCalls(call.Children, initialized)) return true;
        }
        return false;
    }

    private bool ReachesChange(string key, IReadOnlySet<string> initialized)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (changed.Count == 0) return false;
        lock (changeReachabilityLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            changeReachability ??= FindChangeReachability(false);
            if (!changeReachability.Contains(key)) return false;
            if (initialized.Count == 0) return true;
            changesWithoutInitialization ??= FindChangeReachability(true);
        }
        return FindUninitializedChange(key, initialized, new HashSet<string>(StringComparer.Ordinal));
    }

    private HashSet<string> FindChangeReachability(bool skipInitialization)
    {
        var callers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var member in resolvedGraph.Members.Values)
            foreach (var target in ReachabilityTargets(member.Calls, skipInitialization))
            {
                if (!callers.TryGetValue(target, out var incoming)) callers[target] = incoming = new HashSet<string>(StringComparer.Ordinal);
                incoming.Add(member.Key);
            }
        var reachable = new HashSet<string>(changed, StringComparer.Ordinal);
        var pending = new Queue<string>(reachable);
        while (pending.TryDequeue(out var key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!callers.TryGetValue(key, out var incoming)) continue;
            foreach (var caller in incoming)
                if (reachable.Add(caller)) pending.Enqueue(caller);
        }
        return reachable;
    }

    private IEnumerable<string> ReachabilityTargets(IEnumerable<CallStep> calls, bool skipInitialization)
    {
        foreach (var call in calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (skipInitialization && call.IsInitialization) continue;
            if (call.Kind == "call")
            {
                var targets = resolvedGraph.Targets(call, cancellationToken);
                if (targets.Count == 0) yield return call.Key;
                else foreach (var target in targets) yield return target;
            }
            foreach (var target in ReachabilityTargets(call.Children, skipInitialization)) yield return target;
        }
    }

    private bool FindUninitializedChange(string key, IReadOnlySet<string> initialized, HashSet<string> visited)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (changesWithoutInitialization!.Contains(key)) return true;
        if (!changeReachability!.Contains(key) || !visited.Add(key) || !resolvedGraph.Members.TryGetValue(key, out var member)) return false;
        bool CallsReachChange(IEnumerable<CallStep> calls)
        {
            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (call.IsInitialization && call.Children.FirstOrDefault() is { } initializer && initialized.Contains(initializer.Key)) continue;
                if (call.Kind == "call")
                {
                    var targets = resolvedGraph.Targets(call, cancellationToken);
                    if (targets.Count == 0 && FindUninitializedChange(call.Key, initialized, visited)) return true;
                    foreach (var target in targets)
                        if (FindUninitializedChange(target, initialized, visited)) return true;
                }
                if (CallsReachChange(call.Children)) return true;
            }
            return false;
        }
        return CallsReachChange(member.Calls);
    }
}
