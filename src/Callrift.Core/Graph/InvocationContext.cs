namespace Callrift.Core;

internal sealed record InvocationContext(string Key, IReadOnlyDictionary<string, DispatchType> Arguments, bool Limited = false,
    DispatchType? Receiver = null, bool ReceiverSpecialized = false, bool ReceiverExact = false,
    IReadOnlyDictionary<string, DispatchTypeDefinition>? Definitions = null)
{
    public bool HasSpecialization => Arguments.Count != 0 || ReceiverSpecialized;
    public string Identity => Limited ? Key + "\u001e<context-limit>" : !HasSpecialization ? Key : Key + "\u001e" + string.Join(";", Arguments.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + TypeKey(p.Value, Definitions)))
        + (ReceiverSpecialized ? ";this=" + TypeKey(Receiver!, Definitions) + (ReceiverExact ? "!" : "") : "");

    public static InvocationContext Create(CallGraph graph, string key, IReadOnlyDictionary<string, DispatchType> arguments, DispatchType? receiver = null, bool receiverExact = false)
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
        var declaration = graph.ReceiverSensitiveMembers?.Contains(key) == true ? graph.Members.GetValueOrDefault(key)?.InstanceType : null;
        var owner = declaration is null ? null : DispatchTypeCatalog.Substitute(declaration, bindings);
        var instance = owner is null ? null : receiver is null ? owner : Normalize(receiver);
        var ownerExact = owner is not null && graph.TypeDefinitions.GetValueOrDefault(owner.Name)?.IsSealed == true;
        var exact = instance is not null && (receiverExact || graph.TypeDefinitions.GetValueOrDefault(instance.Name)?.IsSealed == true);
        return new(key, bindings, Receiver: instance, ReceiverSpecialized: instance is not null && (TypeKey(instance, graph.TypeDefinitions) != TypeKey(owner!, graph.TypeDefinitions) || exact != ownerExact), ReceiverExact: exact,
            Definitions: graph.TypeDefinitions);
    }

    private static string TypeKey(DispatchType type, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions) => (definitions?.GetValueOrDefault(type.Name)?.ContextIdentity ?? type.Name)
        + "<" + string.Join(",", type.Arguments.Select(argument => TypeKey(argument, definitions))) + ">"
        + (type.Constraints is { } constraints ? $"[{constraints.ReferenceType},{constraints.ValueType},{constraints.UnmanagedType},{constraints.Constructor},{constraints.AllowsRefLikeType}:"
            + string.Join(",", constraints.Types.Select(constraint => TypeKey(constraint, definitions))) + "]" : "");

    public CallStep Resolve(CallStep call) => call with
    {
        DispatchType = call.DispatchType is { } contract ? DispatchTypeCatalog.Substitute(contract, Arguments) : null,
        ReceiverType = call.UsesContainingInstance && Receiver is not null ? Receiver
            : call.ReceiverType is { } receiver ? DispatchTypeCatalog.Substitute(receiver, Arguments) : null,
        InvocationReceiverType = call.UsesContainingInstance && Receiver is not null ? Receiver
            : call.InvocationReceiverType is { } instance ? DispatchTypeCatalog.Substitute(instance, Arguments) : null,
        InvocationReceiverExact = call.UsesContainingInstance && Receiver is not null ? ReceiverExact : call.InvocationReceiverExact,
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
                var instance = candidate.ImplementationType?.Resolve(bindings, "candidate:", []);
                if (call.InvocationReceiverExact && call.InvocationReceiverType is { } expected && instance is not null && !instance.CanUnify(expected)) continue;
                var arguments = candidate.GenericArguments.ToDictionary(p => p.Key, p => p.Value.Resolve(bindings, "candidate:", []), StringComparer.Ordinal);
                if (graph.Members.TryGetValue(candidate.Target, out var member))
                    for (var index = 0; index < member.MethodParameters.Count && index < call.MethodArguments.Count; index++)
                        arguments[member.MethodParameters[index]] = call.MethodArguments[index];
                yield return Create(graph, candidate.Target, arguments, instance, candidate.ImplementationTypeExact);
            }
        }
    }
}
