using Microsoft.CodeAnalysis;

namespace Callrift.Core;

public sealed record DispatchTypeDefinition(DispatchType Type, IReadOnlyList<DispatchType> BaseTypes, bool IsReferenceType, SpecialType SpecialType);

internal static class DispatchTypeCatalog
{
    public static IReadOnlyDictionary<string, DispatchTypeDefinition> Create(IEnumerable<ITypeSymbol> types, CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<string, DispatchTypeDefinition>(StringComparer.Ordinal);
        var pending = new Queue<ITypeSymbol>(types.Distinct<ITypeSymbol>(SymbolEqualityComparer.Default));
        while (pending.TryDequeue(out var type))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type is IArrayTypeSymbol array)
            {
                pending.Enqueue(array.ElementType);
                if (array.BaseType is not null) pending.Enqueue(array.BaseType);
                continue;
            }
            if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
                continue;
            foreach (var argument in named.TypeArguments) pending.Enqueue(argument);
            if (named.ContainingType is not null) pending.Enqueue(named.ContainingType);
            var original = named.OriginalDefinition;
            var shape = DispatchType.From(original);
            if (definitions.ContainsKey(shape.Name)) continue;
            var bases = original.Interfaces.Cast<ITypeSymbol>().ToList();
            if (original.BaseType is not null) bases.Add(original.BaseType);
            definitions.Add(shape.Name, new DispatchTypeDefinition(shape, bases.Select(DispatchType.From).ToArray(), original.IsReferenceType, original.SpecialType));
            foreach (var parent in bases) pending.Enqueue(parent);
        }
        return definitions.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

    public static bool HasReferenceConversion(DispatchType source, DispatchType target, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions,
        HashSet<string>? active = null)
    {
        if (source.IsParameter || target.IsParameter || source.CanUnify(target)) return true;
        if (definitions is null || definitions.Count == 0) return true;
        var sourceArray = IsArray(source);
        var targetArray = IsArray(target);
        definitions.TryGetValue(source.Name, out var sourceDefinition);
        definitions.TryGetValue(target.Name, out var targetDefinition);
        if (!sourceArray && sourceDefinition is { IsReferenceType: false } || !targetArray && targetDefinition is { IsReferenceType: false }) return false;
        if (!sourceArray && sourceDefinition is null || !targetArray && targetDefinition is null) return true;
        if (targetDefinition?.SpecialType == SpecialType.System_Object) return true;
        active ??= new HashSet<string>(StringComparer.Ordinal);
        if (!active.Add(source.StructuralKey() + " -> " + target.StructuralKey())) return false;
        if (sourceArray)
        {
            if (targetArray)
                return source.Name == target.Name && HasReferenceConversion(source.Arguments[0], target.Arguments[0], definitions, active);
            if (source.Name == "array:1" && target.Arguments.Count == 1 && targetDefinition?.SpecialType is
                SpecialType.System_Collections_Generic_IList_T or SpecialType.System_Collections_Generic_ICollection_T
                or SpecialType.System_Collections_Generic_IEnumerable_T or SpecialType.System_Collections_Generic_IReadOnlyList_T
                or SpecialType.System_Collections_Generic_IReadOnlyCollection_T)
                return HasReferenceConversion(source.Arguments[0], target.Arguments[0], definitions, active);
            var arrayBase = definitions.Values.FirstOrDefault(d => d.SpecialType == SpecialType.System_Array);
            return arrayBase is not null && HasReferenceConversion(arrayBase.Type, target, definitions, active);
        }
        if (targetArray) return false;
        if (source.Name == target.Name)
            return source.CanMatch(target, new Dictionary<string, (DispatchType Type, string Side)>(StringComparer.Ordinal), definitions);
        var substitutions = new Dictionary<string, DispatchType>(StringComparer.Ordinal);
        Bind(sourceDefinition!.Type, source, substitutions);
        return sourceDefinition.BaseTypes.Any(parent => HasReferenceConversion(Substitute(parent, substitutions), target, definitions,
            new HashSet<string>(active, StringComparer.Ordinal)));
    }

    private static void Bind(DispatchType template, DispatchType actual, Dictionary<string, DispatchType> substitutions)
    {
        if (template.IsParameter)
        {
            substitutions[template.Name] = actual;
            return;
        }
        for (var i = 0; i < template.Arguments.Count && i < actual.Arguments.Count; i++)
            Bind(template.Arguments[i], actual.Arguments[i], substitutions);
    }

    private static bool IsArray(DispatchType type) => type.Arguments.Count == 1 && type.Name.StartsWith("array:", StringComparison.Ordinal)
        && int.TryParse(type.Name.AsSpan(6), out var rank) && rank > 0;

    private static DispatchType Substitute(DispatchType type, IReadOnlyDictionary<string, DispatchType> substitutions) =>
        type.IsParameter && substitutions.TryGetValue(type.Name, out var replacement) ? replacement
            : type with { Arguments = type.Arguments.Select(t => Substitute(t, substitutions)).ToArray() };
}
