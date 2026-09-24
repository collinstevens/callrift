using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Callrift.Core;

internal sealed class SymbolNames(Func<IMethodSymbol, string>? scope = null, Func<string, string>? path = null)
{
    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static IMethodSymbol Normalize(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    public string Key(IMethodSymbol method)
    {
        method = Normalize(method);
        if (method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner)
            return Key(owner) + "/" + method.Name + "`" + method.Arity + Parameters(method);
        return (scope?.Invoke(method) ?? "source") + "::" + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal)
            + "." + method.MetadataName + (method.Arity == 0 ? "" : "`" + method.Arity) + Parameters(method);
    }

    public string MatchName(IMethodSymbol method)
    {
        method = Normalize(method);
        return method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner
            ? MatchName(owner) + "/" + method.Name
            : (scope is null ? "" : scope(method) + "::") + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;
    }

    public static string Label(IMethodSymbol method)
    {
        method = Normalize(method);
        if (method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner)
            return Label(owner) + "." + method.Name;
        var type = method.ContainingType.ToDisplayString(TypeFormat);
        return method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ? "new " + type : type + "." + method.Name;
    }

    public string Path(string value) => (path?.Invoke(value) ?? value).Replace('\\', '/');

    public SourceLocation Location(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return new SourceLocation(Path(span.Path), span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
    }

    public static string Compact(SyntaxNode node) => string.Join(" ", node.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string SyntaxLabel(SyntaxNode node) => node switch
    {
        InvocationExpressionSyntax invocation => Compact(invocation.Expression),
        ObjectCreationExpressionSyntax creation => "new " + Compact(creation.Type),
        ImplicitObjectCreationExpressionSyntax => "new",
        _ => Compact(node)
    };

    private static string Parameters(IMethodSymbol method) => "(" + string.Join(",", method.Parameters.Select(p =>
        (p.RefKind == RefKind.None ? "" : p.RefKind.ToString().ToLowerInvariant() + " ") + p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")";
}
