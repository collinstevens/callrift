namespace Callrift.Core;

internal static class ContextGraph
{
    private const int MaximumAdditionalStates = 65536;
    private const int MaximumTypeNodes = 128;

    public static CallGraph Create(CallGraph graph, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (graph.Contextual) return graph;
        graph = graph with { ReceiverSensitiveMembers = ReceiverSensitiveMembers(graph, cancellationToken) };
        if (graph.Implementations.Count == 0 && graph.ReceiverSensitiveMembers.Count == 0 && !graph.Members.Values.Any(member => member.GenericParameters.Count != 0)) return graph with { Contextual = true };
        var frames = new Dictionary<string, InvocationContext>(StringComparer.Ordinal);
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        var pending = new Queue<InvocationContext>();
        var omitted = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var additional = 0;
        InvocationContext Add(InvocationContext frame)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frames.TryGetValue(frame.Identity, out var known)) return known;
            var stateLimited = frame.HasSpecialization && additional >= MaximumAdditionalStates;
            var typeLimited = frame.HasSpecialization && TooLarge(!frame.ReceiverSpecialized ? frame.Arguments.Values : frame.Arguments.Values.Append(frame.Receiver!));
            if (stateLimited || typeLimited)
            {
                if (!omitted.TryGetValue(frame.Key, out var reasons)) omitted[frame.Key] = reasons = new SortedSet<string>(StringComparer.Ordinal);
                if (stateLimited) reasons.Add("65536 additional invocation states");
                if (typeLimited) reasons.Add("128 type nodes");
                frame = frame with { Arguments = new Dictionary<string, DispatchType>(), Receiver = null, ReceiverSpecialized = false, ReceiverExact = false, Limited = true };
                if (frames.TryGetValue(frame.Identity, out known)) return known;
            }
            if (frame.HasSpecialization) additional++;
            frames.Add(frame.Identity, frame);
            pending.Enqueue(frame);
            return frame;
        }
        IReadOnlyList<CallStep> Rewrite(IEnumerable<CallStep> calls, InvocationContext frame)
        {
            var result = new List<CallStep>();
            foreach (var original in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var call = frame.Resolve(original);
                var requested = call.Kind == "call" ? InvocationContext.DispatchTargets(graph, call, cancellationToken)
                    .GroupBy(target => target.Identity, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.First()).ToArray() : [];
                var direct = requested.Length == 1 && requested[0].Key == call.Key ? requested[0] : null;
                if (direct is not null) requested = [];
                var semanticTargets = requested.Select(target => target.Identity).ToArray();
                var targets = requested.Select(Add).Select(target => target.Identity).ToArray();
                var key = call.Key;
                var semanticKey = key;
                if (call.Kind == "call" && targets.Length == 0 && graph.Members.ContainsKey(key))
                {
                    var target = direct ?? InvocationContext.Create(graph, key, call.GenericArguments, call.InvocationReceiverType, call.InvocationReceiverExact);
                    semanticKey = target.Identity;
                    key = Add(target).Identity;
                }
                result.Add(call with
                {
                    Key = key,
                    DefinitionKey = original.Key,
                    ContextTargets = targets,
                    SemanticKey = semanticKey,
                    SemanticTargets = semanticTargets,
                    Children = Rewrite(original.Children, frame)
                });
            }
            return result;
        }
        foreach (var key in graph.Members.Keys.Order(StringComparer.Ordinal)) Add(InvocationContext.Create(graph, key, new Dictionary<string, DispatchType>()));
        while (pending.TryDequeue(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var member = graph.Members[frame.Key];
            members.Add(frame.Identity, member with
            {
                Key = frame.Identity,
                DefinitionKey = frame.Key,
                ContextOmitted = frame.Limited,
                Calls = frame.Limited ? [] : Rewrite(member.Calls, frame)
            });
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        var reachedDefinitions = new HashSet<string>(StringComparer.Ordinal);
        void Activate(IEnumerable<string> roots)
        {
            var queue = new Queue<string>(roots);
            while (queue.TryDequeue(out var key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!active.Add(key) || !members.TryGetValue(key, out var member)) continue;
                reachedDefinitions.Add(member.DefinitionKey ?? key);
                foreach (var call in Flatten(member.Calls))
                {
                    if (call.ContextTargets is { Count: > 0 } targets)
                        foreach (var target in targets) queue.Enqueue(target);
                    else if (members.ContainsKey(call.Key)) queue.Enqueue(call.Key);
                }
            }
        }
        IReadOnlyList<string> Roots(IReadOnlyDictionary<string, Member> declarations)
        {
            var seedGraph = graph with { Members = declarations, Contextual = true };
            return EntrySelector.Select(seedGraph, seedGraph, declarations.Keys.ToHashSet(StringComparer.Ordinal), new DiffOptions(), cancellationToken);
        }
        Activate(Roots(graph.Members));
        while (true)
        {
            var remaining = graph.Members.Where(pair => pair.Value.HasBody && !reachedDefinitions.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (remaining.Count == 0) break;
            var roots = Roots(remaining);
            if (roots.Count == 0) throw new InvalidOperationException("Could not select disconnected invocation roots.");
            Activate(roots);
        }
        var diagnostics = graph.Diagnostics.Concat(omitted.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new AnalysisDiagnostic("generic-context-limit",
            "Generic invocation expansion for " + graph.Members[pair.Key].Label + " exceeded " + string.Join(" and ", pair.Value) + ".", graph.Members[pair.Key].Location))).ToArray();
        return graph with
        {
            Members = members,
            Contextual = true,
            ActiveMembers = active,
            ContextTruncated = omitted.Count != 0,
            Diagnostics = diagnostics,
            Coverage = omitted.Count == 0 ? graph.Coverage : graph.Coverage with
            {
                Status = "partial",
                Limitations = graph.Coverage.Limitations.Append("generic-context-limit").Distinct(StringComparer.Ordinal).ToArray()
            }
        };
    }

    private static IEnumerable<CallStep> Flatten(IEnumerable<CallStep> calls)
    {
        foreach (var call in calls)
        {
            if (call.Kind == "call") yield return call;
            foreach (var child in Flatten(call.Children)) yield return child;
        }
    }

    private static bool TooLarge(IEnumerable<DispatchType> types)
    {
        var pending = new Stack<DispatchType>(types);
        var count = 0;
        while (pending.TryPop(out var type))
        {
            if (++count > MaximumTypeNodes) return true;
            foreach (var argument in type.Arguments) pending.Push(argument);
        }
        return false;
    }

    private static IReadOnlySet<string> ReceiverSensitiveMembers(CallGraph graph, CancellationToken cancellationToken)
    {
        var sensitive = new HashSet<string>(StringComparer.Ordinal);
        var callers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var member in graph.Members.Values)
            foreach (var call in Flatten(member.Calls))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!call.UsesContainingInstance) continue;
                if (!call.SuppressDispatch && call.DispatchType is not null) sensitive.Add(member.Key);
                if (!callers.TryGetValue(call.Key, out var incoming)) callers[call.Key] = incoming = new HashSet<string>(StringComparer.Ordinal);
                incoming.Add(member.Key);
            }
        var pending = new Queue<string>(sensitive);
        while (pending.TryDequeue(out var key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!callers.TryGetValue(key, out var incoming)) continue;
            foreach (var caller in incoming)
                if (sensitive.Add(caller)) pending.Enqueue(caller);
        }
        return sensitive;
    }
}
