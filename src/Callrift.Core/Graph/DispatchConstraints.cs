using Microsoft.CodeAnalysis;

namespace Callrift.Core;

public sealed record DispatchConstraints(bool ReferenceType, bool ValueType, bool UnmanagedType, bool Constructor, IReadOnlyList<DispatchType> Types)
{
    public bool AllowsRefLikeType { get; init; }

    internal static bool Allow(DispatchType template, IReadOnlyDictionary<string, (DispatchType Type, string Side)> bindings, string side,
        IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions)
    {
        if (template.IsParameter && template.Constraints is { } constraints)
        {
            var actual = template.Resolve(bindings, side, []);
            if (!actual.IsParameter && definitions is not null && definitions.TryGetValue(actual.Name, out var definition))
            {
                if (definition.IsRefLikeType && !constraints.AllowsRefLikeType) return false;
                if (constraints.ReferenceType && !definition.IsReferenceType) return false;
                if (constraints.ValueType && (!definition.IsValueType || definition.SpecialType == SpecialType.System_Nullable_T)) return false;
                if (constraints.Constructor && !definition.HasPublicParameterlessConstructor) return false;
                if (constraints.UnmanagedType && !IsUnmanaged(actual, definitions, [])) return false;
            }
            else if (DispatchTypeCatalog.IsArray(actual) && (constraints.ValueType || constraints.UnmanagedType || constraints.Constructor)) return false;
            foreach (var required in constraints.Types)
                if (!Assignable(actual, required.Resolve(bindings, side, []), definitions, [])) return false;
        }
        return template.Arguments.All(argument => Allow(argument, bindings, side, definitions));
    }

    private static bool Assignable(DispatchType source, DispatchType target, IReadOnlyDictionary<string, DispatchTypeDefinition>? definitions, HashSet<string> active)
    {
        if (source.IsParameter || target.IsParameter || source.CanUnify(target)) return true;
        if (definitions is null) return true;
        if (DispatchTypeCatalog.IsArray(source)) return DispatchTypeCatalog.HasReferenceConversion(source, target, definitions);
        if (!definitions.TryGetValue(source.Name, out var definition)) return true;
        if (!active.Add(source.StructuralKey() + " -> " + target.StructuralKey())) return false;
        if (source.Name == target.Name)
            return source.CanMatch(target, new Dictionary<string, (DispatchType Type, string Side)>(StringComparer.Ordinal), definitions);
        var substitutions = new Dictionary<string, DispatchType>(StringComparer.Ordinal);
        DispatchTypeCatalog.Bind(definition.Type, source, substitutions);
        return definition.BaseTypes.Any(parent => Assignable(DispatchTypeCatalog.Substitute(parent, substitutions), target, definitions,
            new HashSet<string>(active, StringComparer.Ordinal)));
    }

    private static bool IsUnmanaged(DispatchType type, IReadOnlyDictionary<string, DispatchTypeDefinition> definitions, HashSet<string> active)
    {
        if (type.IsParameter || type.Name == "pointer") return true;
        if (DispatchTypeCatalog.IsArray(type)) return false;
        if (!definitions.TryGetValue(type.Name, out var definition)) return true;
        if (definition.IsUnmanagedType) return true;
        if (!definition.IsValueType || definition.SpecialType == SpecialType.System_Nullable_T) return false;
        if (!active.Add(type.StructuralKey())) return true;
        var substitutions = new Dictionary<string, DispatchType>(StringComparer.Ordinal);
        DispatchTypeCatalog.Bind(definition.Type, type, substitutions);
        return definition.InstanceFields.All(field => IsUnmanaged(DispatchTypeCatalog.Substitute(field, substitutions), definitions,
            new HashSet<string>(active, StringComparer.Ordinal)));
    }
}
