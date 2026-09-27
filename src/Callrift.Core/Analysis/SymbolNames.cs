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

    private static readonly SymbolDisplayFormat DeclarationFormat = SymbolDisplayFormat.CSharpErrorMessageFormat
        .AddParameterOptions(SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeOptionalBrackets | SymbolDisplayParameterOptions.IncludeExtensionThis)
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static IMethodSymbol Normalize(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    public string Key(IMethodSymbol method)
    {
        method = Normalize(method);
        if (interceptors?.TryGetValue(method, out var interceptor) == true) return interceptor.Key;
        if (method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner)
            return Key(owner) + "/" + method.Name + "`" + method.Arity + Parameters(method);
        return (Scope(method) ?? "source") + "::" + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal)
            + "." + method.MetadataName + (method.Arity == 0 ? "" : "`" + method.Arity) + Parameters(method) + ConversionResult(method);
    }

    public string MatchName(IMethodSymbol method)
    {
        method = Normalize(method);
        if (interceptors?.TryGetValue(method, out var interceptor) == true) return interceptor.Key;
        return method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol owner
            ? MatchName(owner) + "/" + method.Name
            : (Scope(method) is { } identity ? identity + "::" : "") + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name + ConversionResult(method);
    }

    private static string ConversionResult(IMethodSymbol method) => method.MethodKind == MethodKind.Conversion
        ? "->" + method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "";

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

    public string Signature(IMethodSymbol method)
    {
        method = Normalize(method);
        if (interceptors?.TryGetValue(method, out var interceptor) == true)
            return interceptor.Label + Parameters(method) + " -> " + method.ReturnType.ToDisplayString();
        var declaration = Modifiers(method) + method.ToDisplayString(DeclarationFormat);
        var returnKind = method.ReturnsByRefReadonly ? "ref readonly " : method.ReturnsByRef ? "ref " : "";
        var signature = declaration + " -> " + returnKind + method.ReturnType.ToDisplayString(DeclarationFormat) + Constraints(method.TypeParameters);
        for (var owner = method.ContainingSymbol; owner is not null; owner = owner.ContainingSymbol)
        {
            var constraints = owner switch
            {
                INamedTypeSymbol type => Constraints(type.TypeParameters),
                IMethodSymbol containing => Constraints(containing.TypeParameters),
                _ => ""
            };
            if (constraints.Length != 0) signature += " [" + owner.ToDisplayString(DeclarationFormat) + constraints + "]";
        }
        return signature;
    }

    private static string Modifiers(IMethodSymbol method)
    {
        var modifiers = new List<string>();
        var access = method.MethodKind is MethodKind.LocalFunction or MethodKind.StaticConstructor ? "" : method.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => ""
        };
        if (access.Length != 0) modifiers.Add(access);
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsAbstract) modifiers.Add("abstract");
        if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsSealed) modifiers.Add("sealed");
        if (method.IsOverride) modifiers.Add("override");
        if (method.IsReadOnly) modifiers.Add("readonly");
        if (method.IsExtern) modifiers.Add("extern");
        if (method.IsAsync) modifiers.Add("async");
        if (method.IsPartialDefinition || method.PartialDefinitionPart is not null) modifiers.Add("partial");
        return modifiers.Count == 0 ? "" : string.Join(" ", modifiers) + " ";
    }

    private static string Constraints(IEnumerable<ITypeParameterSymbol> parameters)
    {
        var clauses = new List<string>();
        foreach (var parameter in parameters)
        {
            var constraints = new List<string>();
            if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
            else if (parameter.HasReferenceTypeConstraint)
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (parameter.HasNotNullConstraint) constraints.Add("notnull");
            else if (parameter.DeclaringMethod?.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax()).OfType<MethodDeclarationSyntax>()
                .SelectMany(declaration => declaration.ConstraintClauses).Any(clause => clause.Name.Identifier.ValueText == parameter.Name
                    && clause.Constraints.OfType<DefaultConstraintSyntax>().Any()) == true) constraints.Add("default");
            constraints.AddRange(parameter.ConstraintTypes.OrderBy(type => type.TypeKind == TypeKind.Interface ? 1 : 0)
                .ThenBy(type => type.ToDisplayString(DeclarationFormat), StringComparer.Ordinal).Select(type => type.ToDisplayString(DeclarationFormat)));
            if (parameter.HasConstructorConstraint) constraints.Add("new()");
            if (parameter.AllowsRefLikeType) constraints.Add("allows ref struct");
            if (constraints.Count != 0) clauses.Add(" where " + parameter.ToDisplayString(DeclarationFormat) + " : " + string.Join(", ", constraints));
        }
        return string.Concat(clauses);
    }

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
        if (method.DeclaringSyntaxReferences.FirstOrDefault() is { } entry && entry.GetSyntax() is CompilationUnitSyntax)
            return (identity ?? "source") + "/file:" + (declaringPath?.Invoke(method, entry.SyntaxTree.FilePath) ?? Path(entry.SyntaxTree.FilePath));
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

    public static string Compact(SyntaxNode node) => node is IdentifierNameSyntax name ? name.Identifier.Text
        : node.ReplaceTrivia(node.DescendantTrivia(), static (_, _) => default)
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
