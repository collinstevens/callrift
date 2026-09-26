using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Callrift.Core;

internal sealed class SymbolNames(Func<IMethodSymbol, string>? scope = null, Func<string, string>? path = null,
    IReadOnlyDictionary<IMethodSymbol, InterceptorIdentity>? interceptors = null, Func<IMethodSymbol, string, string>? declaringPath = null,
    Func<INamedTypeSymbol, string, string>? typeDeclaringPath = null, Func<INamedTypeSymbol, string>? typeScope = null)
{
    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static IMethodSymbol Normalize(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    public string Key(IMethodSymbol method)
    {
        method = Normalize(method);
        if (interceptors?.TryGetValue(method, out var interceptor) == true) return interceptor.Key;
        if (method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner)
            return Key(owner) + "/" + method.Name + "`" + method.Arity + Parameters(method);
        return (Scope(method) ?? "source") + "::" + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal)
            + "." + method.MetadataName + (method.Arity == 0 ? "" : "`" + method.Arity) + Parameters(method);
    }

    public string MatchName(IMethodSymbol method)
    {
        method = Normalize(method);
        if (interceptors?.TryGetValue(method, out var interceptor) == true) return interceptor.Key;
        return method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner
            ? MatchName(owner) + "/" + method.Name
            : (Scope(method) is { } identity ? identity + "::" : "") + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;
    }

    public string Label(IMethodSymbol method)
    {
        method = Normalize(method);
        if (interceptors?.TryGetValue(method, out var interceptor) == true) return interceptor.Label;
        if (method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner)
            return Label(owner) + "." + method.Name;
        var type = method.ContainingType.ToDisplayString(TypeFormat);
        if (method is { Name: "<Clone>$", ContainingType.IsRecord: true }) return "clone " + type;
        return method.MethodKind == MethodKind.StaticConstructor ? "initialization of " + type
            : method.MethodKind == MethodKind.Constructor ? "new " + type : type + "." + method.Name;
    }

    public string Signature(IMethodSymbol method) =>
        (interceptors?.TryGetValue(Normalize(method), out var interceptor) == true
            ? interceptor.Label + Parameters(method)
            : method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)) + " -> " + method.ReturnType.ToDisplayString();

    public string Path(string value) => (path?.Invoke(value) ?? value).Replace('\\', '/');

    public string? InitializationScope(INamedTypeSymbol type)
    {
        var method = type.StaticConstructors.FirstOrDefault() ?? type.GetMembers().OfType<IMethodSymbol>().FirstOrDefault();
        var identity = typeScope?.Invoke(type) ?? (scope is null ? "source" : method is null ? null : scope(method));
        if (identity is null) return null;
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.IsFileLocal && current.DeclaringSyntaxReferences.FirstOrDefault() is { } declaration)
                return identity + "/file:" + (typeDeclaringPath?.Invoke(current, declaration.SyntaxTree.FilePath) ?? Path(declaration.SyntaxTree.FilePath));
        return identity;
    }

    public string? TypeIdentity(INamedTypeSymbol type)
    {
        var identity = type.ContainingAssembly.Identity.Name + "::" + type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var fileLocal = false;
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.IsFileLocal && current.DeclaringSyntaxReferences.FirstOrDefault() is { } declaration)
            {
                fileLocal = true;
                identity += "/file:" + (typeDeclaringPath?.Invoke(current, declaration.SyntaxTree.FilePath) ?? Path(declaration.SyntaxTree.FilePath));
            }
        return fileLocal ? identity : null;
    }

    private string? Scope(IMethodSymbol method)
    {
        var identity = scope?.Invoke(method);
        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
            if (type.IsFileLocal && type.DeclaringSyntaxReferences.FirstOrDefault() is { } declaration)
                return (identity ?? "source") + "/file:" + (declaringPath?.Invoke(method, declaration.SyntaxTree.FilePath) ?? Path(declaration.SyntaxTree.FilePath));
        return identity;
    }

    public SourceLocation Location(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return new SourceLocation(Path(span.Path), span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
    }

    public static string Compact(SyntaxNode node) => node.ReplaceTrivia(node.DescendantTrivia(), static (_, _) => default)
        .NormalizeWhitespace(indentation: "", eol: " ", elasticTrivia: false).ToFullString();

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
