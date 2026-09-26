using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Callrift.Core;

internal static class ConstructorBindings
{
    public static IReadOnlyDictionary<IMethodSymbol, IMethodSymbol?> Create(CSharpCompilation compilation, IEnumerable<INamedTypeSymbol> types, CancellationToken cancellationToken)
    {
        var included = types.ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var requests = new List<(IMethodSymbol Constructor, SyntaxAnnotation Annotation, SyntaxTree Tree)>();
        var result = new Dictionary<IMethodSymbol, IMethodSymbol?>(SymbolEqualityComparer.Default);
        var rewrittenTrees = new Dictionary<SyntaxTree, SyntaxTree>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToArray();
            var rewritten = root.ReplaceNodes(declarations, (original, current) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(original, cancellationToken) is not INamedTypeSymbol { TypeKind: TypeKind.Class } type || !included.Contains(type)) return current;
                var primary = type.InstanceConstructors.SingleOrDefault(constructor => !constructor.IsImplicitlyDeclared
                    && constructor.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree == tree && reference.Span == original.Span));
                if (primary is not null && model.GetOperation(original, cancellationToken) is null)
                {
                    var baseType = original.BaseList?.Types.OfType<SimpleBaseTypeSyntax>().FirstOrDefault(baseType => model.GetTypeInfo(baseType.Type, cancellationToken).Type?.TypeKind == TypeKind.Class);
                    if (baseType is not null)
                    {
                        var annotation = new SyntaxAnnotation();
                        var index = original.BaseList!.Types.IndexOf(baseType);
                        var currentBase = current.BaseList!.Types[index];
                        current = current.ReplaceNode(currentBase, SyntaxFactory.PrimaryConstructorBaseType(currentBase.Type, SyntaxFactory.ArgumentList())).WithAdditionalAnnotations(annotation);
                        requests.Add((primary, annotation, tree));
                    }
                    else
                        result[primary] = type.BaseType?.InstanceConstructors.SingleOrDefault(constructor => constructor.Parameters.Length == 0);
                }
                var first = type.DeclaringSyntaxReferences.First(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax);
                if (first.SyntaxTree != tree || first.Span != original.Span) return current;
                foreach (var constructor in type.InstanceConstructors.Where(constructor => constructor.IsImplicitlyDeclared))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var copy = IsCopy(constructor);
                    if (!copy && constructor.Parameters.Length != 0) continue;
                    if (type.BaseType?.SpecialType == SpecialType.System_Object)
                    {
                        result[constructor] = type.BaseType.InstanceConstructors.SingleOrDefault(target => target.Parameters.Length == 0);
                        continue;
                    }
                    var declaration = SyntaxFactory.ConstructorDeclaration(current.Identifier).WithBody(SyntaxFactory.Block());
                    var accessibility = constructor.DeclaredAccessibility switch
                    {
                        Accessibility.Protected => SyntaxKind.ProtectedKeyword,
                        Accessibility.Private => SyntaxKind.PrivateKeyword,
                        _ => SyntaxKind.PublicKeyword
                    };
                    declaration = declaration.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(accessibility)));
                    if (copy)
                    {
                        declaration = declaration.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Parameter(SyntaxFactory.Identifier("original")).WithType(SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))))));
                        declaration = declaration.WithInitializer(SyntaxFactory.ConstructorInitializer(SyntaxKind.BaseConstructorInitializer,
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("original"))))));
                    }
                    var annotation = new SyntaxAnnotation();
                    declaration = declaration.NormalizeWhitespace().WithAdditionalAnnotations(annotation);
                    if (current is RecordDeclarationSyntax record && record.OpenBraceToken.IsKind(SyntaxKind.None))
                        current = record.WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)).WithSemicolonToken(default);
                    current = current.AddMembers(declaration);
                    requests.Add((constructor, annotation, tree));
                }
                return current;
            });
            if (rewritten != root) rewrittenTrees.Add(tree, tree.WithRootAndOptions(rewritten, tree.Options));
        }
        foreach (var (original, rewritten) in rewrittenTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            compilation = compilation.ReplaceSyntaxTree(original, rewritten);
        }
        foreach (var (original, tree) in rewrittenTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            foreach (var request in requests.Where(request => request.Tree == original))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = root.GetAnnotatedNodes(request.Annotation).SingleOrDefault();
                if (declaration is not null) result.Add(request.Constructor, Initializer(model, declaration, cancellationToken));
            }
        }
        return result;
    }

    public static bool IsCopy(IMethodSymbol constructor) => constructor.ContainingType is { IsRecord: true, TypeKind: TypeKind.Class }
        && constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, constructor.ContainingType);

    public static IMethodSymbol? Initializer(SemanticModel model, SyntaxNode declaration, CancellationToken cancellationToken) =>
        model.GetOperation(declaration, cancellationToken) is IConstructorBodyOperation { Initializer: { } initializer }
            ? Invocations(initializer).FirstOrDefault(invocation => invocation.TargetMethod.MethodKind == MethodKind.Constructor)?.TargetMethod : null;

    private static IEnumerable<IInvocationOperation> Invocations(IOperation operation)
    {
        if (operation is IInvocationOperation invocation) yield return invocation;
        foreach (var child in operation.ChildOperations)
            foreach (var nested in Invocations(child)) yield return nested;
    }
}
