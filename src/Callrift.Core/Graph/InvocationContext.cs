using System.Collections.Immutable;
using System.Runtime.CompilerServices;

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
        var result = new DefaultInterpolatedStringHandler(0, 0, null, stackalloc char[256]);
        try
        {
            result.AppendLiteral(Key);
            result.AppendLiteral("\u001e");
            var first = true;
            foreach (var argument in Arguments.Count > 1 ? Arguments.OrderBy(p => p.Key, StringComparer.Ordinal) : Arguments.AsEnumerable())
            {
                if (!first) result.AppendLiteral(";");
                first = false;
                result.AppendLiteral(argument.Key);
                result.AppendLiteral("=");
                AppendTypeKey(ref result, argument.Value, Definitions);
            }
            if (ReceiverSpecialized)
            {
                result.AppendLiteral(";this=");
                AppendTypeKey(ref result, Receiver!, Definitions);
                if (ReceiverExact) result.AppendLiteral("!");
            }
            return result.ToString();
        }
        finally
        {
            result.Clear();
        }
    }

    public static InvocationContext Create(CallGraph graph, string key, IReadOnlyDictionary<string, DispatchType> arguments, DispatchType? receiver = null, bool receiverExact = false)
    {
        var member = graph.Members.GetValueOrDefault(key);
        if (member is null || member.GenericParameters.Count == 0 && (member.InstanceType is null || graph.ReceiverSensitiveMembers?.Contains(key) != true))
            return new(key, ImmutableDictionary<string, DispatchType>.Empty, Definitions: graph.TypeDefinitions);
        return CreateSpecialized(graph, key, arguments, receiver, receiverExact);
    }

    private static InvocationContext CreateSpecialized(CallGraph graph, string key, IReadOnlyDictionary<string, DispatchType> arguments, DispatchType? receiver, bool receiverExact)
    {
        var parameters = graph.Members.GetValueOrDefault(key)?.GenericParameters ?? [];
        Dictionary<string, string>? names = null;
        DispatchType Normalize(DispatchType type)
        {
            var name = type.Name;
            if (type.IsParameter)
            {
                names ??= new Dictionary<string, string>(StringComparer.Ordinal);
                if (!names.TryGetValue(name, out name))
                    names[type.Name] = name = names.Count < parameters.Count ? parameters[names.Count] : "generic-context:" + names.Count;
            }
            var normalizedArguments = NormalizeTypes(type.Arguments);
            var constraints = type.Constraints;
            if (constraints is not null)
            {
                var types = NormalizeTypes(constraints.Types);
                if (!ReferenceEquals(types, constraints.Types)) constraints = constraints with { Types = types };
            }
            return name == type.Name && ReferenceEquals(normalizedArguments, type.Arguments) && ReferenceEquals(constraints, type.Constraints)
                ? type : type with { Name = name!, Arguments = normalizedArguments, Constraints = constraints };
        }
        IReadOnlyList<DispatchType> NormalizeTypes(IReadOnlyList<DispatchType> types)
        {
            DispatchType[]? result = null;
            for (var index = 0; index < types.Count; index++)
            {
                var normalized = Normalize(types[index]);
                if (ReferenceEquals(normalized, types[index])) continue;
                result ??= types.ToArray();
                result[index] = normalized;
            }
            return result ?? types;
        }
        Dictionary<string, DispatchType>? specializedBindings = null;
        foreach (var parameter in parameters)
        {
            var normalized = Normalize(arguments.GetValueOrDefault(parameter) ?? new DispatchType(parameter, [], true));
            if (normalized.IsParameter && normalized.Name == parameter && normalized.Constraints is null) continue;
            specializedBindings ??= new Dictionary<string, DispatchType>(StringComparer.Ordinal);
            specializedBindings.Add(parameter, normalized);
        }
        IReadOnlyDictionary<string, DispatchType> bindings = specializedBindings is null ? ImmutableDictionary<string, DispatchType>.Empty : specializedBindings;
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

    private static void AppendTypeKey(ref DefaultInterpolatedStringHandler result, DispatchType type, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions)
    {
        result.AppendLiteral(definitions?.GetValueOrDefault(type.Name)?.ContextIdentity ?? type.Name);
        result.AppendLiteral("<");
        AppendTypes(ref result, type.Arguments, definitions);
        result.AppendLiteral(">");
        if (type.Constraints is not { } constraints) return;
        result.AppendLiteral("[");
        result.AppendFormatted(constraints.ReferenceType);
        result.AppendLiteral(",");
        result.AppendFormatted(constraints.ValueType);
        result.AppendLiteral(",");
        result.AppendFormatted(constraints.UnmanagedType);
        result.AppendLiteral(",");
        result.AppendFormatted(constraints.Constructor);
        result.AppendLiteral(",");
        result.AppendFormatted(constraints.AllowsRefLikeType);
        result.AppendLiteral(":");
        AppendTypes(ref result, constraints.Types, definitions);
        result.AppendLiteral("]");
    }

    private static void AppendTypes(ref DefaultInterpolatedStringHandler result, IReadOnlyList<DispatchType> types, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions)
    {
        for (var index = 0; index < types.Count; index++)
        {
            if (index != 0) result.AppendLiteral(",");
            AppendTypeKey(ref result, types[index], definitions);
        }
    }

    public CallStep Resolve(CallStep call) => Arguments.Count == 0 && (!call.UsesContainingInstance || Receiver is null) ? call : call with
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
                var instance = bindings.Count == 0 ? candidate.ImplementationType : candidate.ImplementationType?.Resolve(bindings, "candidate:", []);
                if (call.InvocationReceiverExact && call.InvocationReceiverType is { } expected && instance is not null && !instance.CanUnify(expected)) continue;
                var arguments = candidate.GenericArguments.Count == 0 ? null
                    : candidate.GenericArguments.ToDictionary(p => p.Key, p => bindings.Count == 0 ? p.Value : p.Value.Resolve(bindings, "candidate:", []), StringComparer.Ordinal);
                if (graph.Members.TryGetValue(candidate.Target, out var member))
                    for (var index = 0; index < member.MethodParameters.Count && index < call.MethodArguments.Count; index++)
                        (arguments ??= new Dictionary<string, DispatchType>(StringComparer.Ordinal))[member.MethodParameters[index]] = call.MethodArguments[index];
                yield return Create(graph, candidate.Target, arguments is null ? ImmutableDictionary<string, DispatchType>.Empty : arguments, instance, candidate.ImplementationTypeExact);
            }
        }
    }
}
