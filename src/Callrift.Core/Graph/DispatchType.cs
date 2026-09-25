using Microsoft.CodeAnalysis;

namespace Callrift.Core;

public sealed record DispatchType(string Name, IReadOnlyList<DispatchType> Arguments, bool IsParameter = false, IReadOnlyList<bool>? VariantArguments = null)
{
    public IReadOnlyList<VarianceKind>? VarianceDirections { get; init; }

    internal static DispatchType From(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol parameter => new(parameter.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + parameter.Ordinal, [], true),
        IArrayTypeSymbol array => new("array:" + array.Rank, [From(array.ElementType)]),
        IPointerTypeSymbol pointer => new("pointer", [From(pointer.PointedAtType)]),
        IDynamicTypeSymbol => new("special:System_Object", []) { VarianceDirections = [] },
        INamedTypeSymbol named when named.TypeKind != TypeKind.Error => Named(named.TupleUnderlyingType ?? named),
        _ => new("", [], true)
    };

    private static DispatchType Named(INamedTypeSymbol type)
    {
        var arguments = type.TypeArguments.Select(From).ToList();
        var variance = type.TypeParameters.Select(p => p.Variance != VarianceKind.None).ToList();
        var directions = type.TypeParameters.Select(p => p.Variance).ToList();
        if (type.ContainingType is { } containing)
        {
            arguments.Insert(0, From(containing));
            variance.Insert(0, false);
            directions.Insert(0, VarianceKind.None);
        }
        var identity = type.SpecialType == SpecialType.System_Object ? "special:System_Object"
            : type.ContainingAssembly.Identity.Name + "::" + type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        for (var containingType = type; containingType is not null; containingType = containingType.ContainingType)
            if (containingType.IsFileLocal) identity += "/file:" + containingType.MetadataName;
        return new(identity, arguments, VariantArguments: variance) { VarianceDirections = directions };
    }

    public bool CanMatch(DispatchType receiver) => CanMatch(receiver, new Dictionary<string, (DispatchType Type, string Side)>(StringComparer.Ordinal));

    internal bool CanMatch(DispatchType receiver, Dictionary<string, (DispatchType Type, string Side)> bindings,
        IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions = null)
    {
        if (Name != receiver.Name || Arguments.Count != receiver.Arguments.Count) return false;
        for (var i = 0; i < Arguments.Count; i++)
        {
            var direction = VarianceDirections?[i] ?? VarianceKind.None;
            if (direction != VarianceKind.None)
            {
                var candidate = Arguments[i].Resolve(bindings, "candidate:", []);
                var expected = receiver.Arguments[i].Resolve(bindings, "receiver:", []);
                if (!DispatchTypeCatalog.HasReferenceConversion(direction == VarianceKind.Out ? candidate : expected,
                    direction == VarianceKind.Out ? expected : candidate, definitions)) return false;
            }
            else if (VarianceDirections is not null || VariantArguments is not { } variance || !variance[i])
                if (!Unify(Arguments[i], "candidate:", receiver.Arguments[i], "receiver:", bindings, [])) return false;
        }
        return true;
    }

    internal bool CanUnify(DispatchType other) => Unify(this, "candidate:", other, "receiver:", new Dictionary<string, (DispatchType Type, string Side)>(StringComparer.Ordinal), []);

    internal string StructuralKey() => Name + "<" + string.Join(",", Arguments.Select(a => a.StructuralKey())) + ">";

    private DispatchType Resolve(IReadOnlyDictionary<string, (DispatchType Type, string Side)> bindings, string side, HashSet<string> active)
    {
        if (IsParameter && active.Add(side + Name) && bindings.TryGetValue(side + Name, out var bound))
            return bound.Type.Resolve(bindings, bound.Side, active);
        return this with { Arguments = Arguments.Select(a => a.Resolve(bindings, side, new HashSet<string>(active, StringComparer.Ordinal))).ToArray() };
    }

    private static bool Unify(DispatchType left, string leftSide, DispatchType right, string rightSide,
        Dictionary<string, (DispatchType Type, string Side)> bindings, HashSet<string> active)
    {
        if (left.IsParameter)
        {
            if (left.Name.Length == 0) return true;
            var key = leftSide + left.Name;
            if (!active.Add(key)) return true;
            if (bindings.TryGetValue(key, out var bound))
                return Unify(bound.Type, bound.Side, right, rightSide, bindings, active);
            bindings[key] = (right, rightSide);
            return true;
        }
        if (right.IsParameter) return Unify(right, rightSide, left, leftSide, bindings, active);
        if (left.Name != right.Name || left.Arguments.Count != right.Arguments.Count) return false;
        for (var i = 0; i < left.Arguments.Count; i++)
            if (!Unify(left.Arguments[i], leftSide, right.Arguments[i], rightSide, bindings, new HashSet<string>(active, StringComparer.Ordinal))) return false;
        return true;
    }
}

public sealed record DispatchContract(string Target, DispatchType Type, IReadOnlyList<DispatchType>? ReceiverTypes = null)
{
    public bool CanMatch(DispatchType contract, DispatchType? receiver) => CanMatch(contract, receiver, null);

    public bool CanMatch(DispatchType contract, DispatchType? receiver, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions)
    {
        var bindings = new Dictionary<string, (DispatchType Type, string Side)>(StringComparer.Ordinal);
        if (!Type.CanMatch(contract, bindings, definitions)) return false;
        return receiver is null || ReceiverTypes is null || ReceiverTypes.Any(t =>
        {
            var constrained = new Dictionary<string, (DispatchType Type, string Side)>(bindings, StringComparer.Ordinal);
            return t.CanMatch(receiver, constrained, definitions) && Type.CanMatch(contract, constrained, definitions);
        });
    }
}

public sealed record DispatchMap(IReadOnlyDictionary<string, IReadOnlyList<string>> Implementations,
    IReadOnlyDictionary<string, IReadOnlyList<DispatchContract>> Contracts)
{
    public IReadOnlyDictionary<string, DispatchTypeDefinition> TypeDefinitions { get; init; } = new Dictionary<string, DispatchTypeDefinition>();
}
