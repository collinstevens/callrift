using System.Text;

namespace Callrift.Core;

internal sealed class InvocationContext(string Key, IReadOnlyDictionary<string, DispatchType> Arguments, bool Limited = false,
    DispatchType? Receiver = null, bool ReceiverSpecialized = false, bool ReceiverExact = false,
    IReadOnlyDictionary<string, DispatchTypeDefinition>? Definitions = null)
{
    public string Key { get; } = Key;
    public IReadOnlyDictionary<string, DispatchType> Arguments { get; } = Arguments;
    public bool Limited { get; } = Limited;
    public DispatchType? Receiver { get; } = Receiver;
    public bool ReceiverSpecialized { get; } = ReceiverSpecialized;
    public bool ReceiverExact { get; } = ReceiverExact;
    public IReadOnlyDictionary<string, DispatchTypeDefinition>? Definitions { get; } = Definitions;
    private string? identity;

    public bool HasSpecialization => Arguments.Count != 0 || ReceiverSpecialized;
    public string Identity => identity ??= CreateIdentity();

    private string CreateIdentity()
    {
        if (Limited) return Key + "\u001e<context-limit>";
        if (!HasSpecialization) return Key;
        var result = new StringBuilder(Key).Append('\u001e');
        var first = true;
        foreach (var argument in Arguments.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!first) result.Append(';');
            first = false;
            result.Append(argument.Key).Append('=');
            AppendTypeKey(result, argument.Value, Definitions);
        }
        if (ReceiverSpecialized)
        {
            result.Append(";this=");
            AppendTypeKey(result, Receiver!, Definitions);
            if (ReceiverExact) result.Append('!');
        }
        return result.ToString();
    }

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
        return new(key, bindings, Receiver: instance, ReceiverSpecialized: instance is not null && (exact != ownerExact || !SameType(instance, owner!, graph.TypeDefinitions)), ReceiverExact: exact,
            Definitions: graph.TypeDefinitions);
    }

    private static bool SameType(DispatchType left, DispatchType right, IReadOnlyDictionary<string, DispatchTypeDefinition> definitions)
    {
        if (ReferenceEquals(left, right)) return true;
        if ((definitions.GetValueOrDefault(left.Name)?.ContextIdentity ?? left.Name) != (definitions.GetValueOrDefault(right.Name)?.ContextIdentity ?? right.Name)
            || left.Arguments.Count != right.Arguments.Count) return false;
        for (var index = 0; index < left.Arguments.Count; index++)
            if (!SameType(left.Arguments[index], right.Arguments[index], definitions)) return false;
        var leftConstraints = left.Constraints;
        var rightConstraints = right.Constraints;
        if (ReferenceEquals(leftConstraints, rightConstraints)) return true;
        if (leftConstraints is null || rightConstraints is null || leftConstraints.ReferenceType != rightConstraints.ReferenceType
            || leftConstraints.ValueType != rightConstraints.ValueType || leftConstraints.UnmanagedType != rightConstraints.UnmanagedType
            || leftConstraints.Constructor != rightConstraints.Constructor || leftConstraints.AllowsRefLikeType != rightConstraints.AllowsRefLikeType
            || leftConstraints.Types.Count != rightConstraints.Types.Count) return false;
        for (var index = 0; index < leftConstraints.Types.Count; index++)
            if (!SameType(leftConstraints.Types[index], rightConstraints.Types[index], definitions)) return false;
        return true;
    }

    private static void AppendTypeKey(StringBuilder result, DispatchType type, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions)
    {
        result.Append(definitions?.GetValueOrDefault(type.Name)?.ContextIdentity ?? type.Name).Append('<');
        AppendTypes(result, type.Arguments, definitions);
        result.Append('>');
        if (type.Constraints is not { } constraints) return;
        result.Append('[').Append(constraints.ReferenceType).Append(',').Append(constraints.ValueType).Append(',')
            .Append(constraints.UnmanagedType).Append(',').Append(constraints.Constructor).Append(',').Append(constraints.AllowsRefLikeType).Append(':');
        AppendTypes(result, constraints.Types, definitions);
        result.Append(']');
    }

    private static void AppendTypes(StringBuilder result, IReadOnlyList<DispatchType> types, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions)
    {
        for (var index = 0; index < types.Count; index++)
        {
            if (index != 0) result.Append(',');
            AppendTypeKey(result, types[index], definitions);
        }
    }

    public CallStep Resolve(CallStep call) => call with
    {
        DispatchType = call.DispatchType is { } contract ? DispatchTypeCatalog.Substitute(contract, Arguments) : null,
        ReceiverType = call.UsesContainingInstance && Receiver is not null ? Receiver
            : call.ReceiverType is { } receiver ? DispatchTypeCatalog.Substitute(receiver, Arguments) : null,
        InvocationReceiverType = call.UsesContainingInstance && Receiver is not null ? Receiver
            : call.InvocationReceiverType is { } instance ? DispatchTypeCatalog.Substitute(instance, Arguments) : null,
        InvocationReceiverExact = call.UsesContainingInstance && Receiver is not null ? ReceiverExact : call.InvocationReceiverExact,
        GenericArguments = Arguments.Count == 0 || call.GenericArguments.Count == 0 ? call.GenericArguments : call.GenericArguments.ToDictionary(p => p.Key, p => DispatchTypeCatalog.Substitute(p.Value, Arguments), StringComparer.Ordinal),
        MethodArguments = Arguments.Count == 0 || call.MethodArguments.Count == 0 ? call.MethodArguments : call.MethodArguments.Select(t => DispatchTypeCatalog.Substitute(t, Arguments)).ToArray()
    };

    internal static IEnumerable<InvocationContext> DispatchTargets(CallGraph graph, CallStep call, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (call.SuppressDispatch) yield break;
        if (call.DispatchType is null || !graph.DispatchContracts.TryGetValue(call.Key, out var contracts))
        {
            if (!graph.Implementations.TryGetValue(call.Key, out var targets)) yield break;
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
