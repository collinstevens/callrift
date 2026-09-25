using Microsoft.CodeAnalysis;

namespace Callrift.Core;

internal static class GenericBindings
{
    public static IReadOnlyDictionary<string, DispatchType> FromMethod(IMethodSymbol method)
    {
        var bindings = new Dictionary<string, DispatchType>(StringComparer.Ordinal);
        DispatchTypeCatalog.Bind(DispatchType.From(method.ContainingType.OriginalDefinition), DispatchType.From(method.ContainingType), bindings);
        for (var current = method; current is not null; current = current.ContainingSymbol as IMethodSymbol)
            for (var index = 0; index < current.TypeParameters.Length; index++)
                bindings[DispatchType.From(current.OriginalDefinition.TypeParameters[index]).Name] = DispatchType.From(current.TypeArguments[index]);
        return bindings;
    }
}
