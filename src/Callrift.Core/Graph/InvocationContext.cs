namespace Callrift.Core;

internal sealed record InvocationContext(string Key, IReadOnlyDictionary<string, DispatchType> Arguments, bool Limited = false)
{
    public string Identity => Limited ? Key + "\u001e<context-limit>" : Arguments.Count == 0 ? Key : Key + "\u001e" + string.Join(";", Arguments.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + TypeKey(p.Value)));

    public static InvocationContext Create(CallGraph graph, string key, IReadOnlyDictionary<string, DispatchType> arguments)
    {
        var parameters = graph.Members.GetValueOrDefault(key)?.GenericParameters ?? [];
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        DispatchType Normalize(DispatchType type)
        {
            var name = type.Name;
            if (type.IsParameter && !names.TryGetValue(name, out name))
                names[type.Name] = name = names.Count < parameters.Count ? parameters[names.Count] : "generic-context:" + names.Count;
            return type with
            {
                Name = name!,
                Arguments = type.Arguments.Select(Normalize).ToArray(),
                Constraints = type.Constraints is { } constraints ? constraints with { Types = constraints.Types.Select(Normalize).ToArray() } : null
            };
        }
        var bindings = parameters.Select(parameter => new KeyValuePair<string, DispatchType>(parameter,
            Normalize(arguments.GetValueOrDefault(parameter) ?? new DispatchType(parameter, [], true)))).Where(p =>
                !(p.Value.IsParameter && p.Value.Name == p.Key && p.Value.Constraints is null)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return new(key, bindings);
    }

    private static string TypeKey(DispatchType type) => type.Name + "<" + string.Join(",", type.Arguments.Select(TypeKey)) + ">"
        + (type.Constraints is { } constraints ? $"[{constraints.ReferenceType},{constraints.ValueType},{constraints.UnmanagedType},{constraints.Constructor},{constraints.AllowsRefLikeType}:"
            + string.Join(",", constraints.Types.Select(TypeKey)) + "]" : "");

    public CallStep Resolve(CallStep call) => call with
    {
        DispatchType = call.DispatchType is { } contract ? DispatchTypeCatalog.Substitute(contract, Arguments) : null,
        ReceiverType = call.ReceiverType is { } receiver ? DispatchTypeCatalog.Substitute(receiver, Arguments) : null,
        GenericArguments = call.GenericArguments.ToDictionary(p => p.Key, p => DispatchTypeCatalog.Substitute(p.Value, Arguments), StringComparer.Ordinal),
        MethodArguments = call.MethodArguments.Select(t => DispatchTypeCatalog.Substitute(t, Arguments)).ToArray()
    };

    internal static IEnumerable<InvocationContext> DispatchTargets(CallGraph graph, CallStep call, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (call.SuppressDispatch || !graph.Implementations.TryGetValue(call.Key, out var targets)) yield break;
        if (call.DispatchType is null || !graph.DispatchContracts.TryGetValue(call.Key, out var contracts))
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return Create(graph, target, call.GenericArguments);
            }
            yield break;
        }
        foreach (var candidate in contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var bindings in candidate.Bind(call.DispatchType, call.ReceiverType, graph.TypeDefinitions, cancellationToken))
            {
                var arguments = candidate.GenericArguments.ToDictionary(p => p.Key, p => p.Value.Resolve(bindings, "candidate:", []), StringComparer.Ordinal);
                if (graph.Members.TryGetValue(candidate.Target, out var member))
                    for (var index = 0; index < member.MethodParameters.Count && index < call.MethodArguments.Count; index++)
                        arguments[member.MethodParameters[index]] = call.MethodArguments[index];
                yield return Create(graph, candidate.Target, arguments);
            }
        }
    }
}
