using Microsoft.CodeAnalysis;

namespace Callrift.Core;

public sealed record DispatchType(string Name, IReadOnlyList<DispatchType> Arguments, bool IsParameter = false, IReadOnlyList<bool>? VariantArguments = null)
{
    internal static DispatchType From(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol parameter => new(parameter.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + parameter.Ordinal, [], true),
        IArrayTypeSymbol array => new("array:" + array.Rank, [From(array.ElementType)]),
        IPointerTypeSymbol pointer => new("pointer", [From(pointer.PointedAtType)]),
        INamedTypeSymbol named when named.TypeKind != TypeKind.Error => Named(named.IsTupleType ? named.TupleUnderlyingType! : named),
        _ => new("", [], true)
    };

    private static DispatchType Named(INamedTypeSymbol type)
    {
        var arguments = type.TypeArguments.Select(From).ToList();
        var variance = type.TypeParameters.Select(p => p.Variance != VarianceKind.None).ToList();
        if (type.ContainingType is { } containing)
        {
            arguments.Insert(0, From(containing));
            variance.Insert(0, false);
        }
        return new(type.ContainingAssembly.Identity.Name + "::" + type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), arguments, VariantArguments: variance);
    }

    public bool CanMatch(DispatchType receiver)
    {
        if (Name != receiver.Name || Arguments.Count != receiver.Arguments.Count) return false;
        var bindings = new Dictionary<string, (DispatchType Type, string Side)>(StringComparer.Ordinal);
        for (var i = 0; i < Arguments.Count; i++)
            if (VariantArguments is not { } variance || !variance[i])
                if (!Unify(Arguments[i], "candidate:", receiver.Arguments[i], "receiver:", bindings, [])) return false;
        return true;
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

public sealed record DispatchContract(string Target, DispatchType Type);

public sealed record DispatchMap(IReadOnlyDictionary<string, IReadOnlyList<string>> Implementations,
    IReadOnlyDictionary<string, IReadOnlyList<DispatchContract>> Contracts);
