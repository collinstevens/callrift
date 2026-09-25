using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Callrift.Core;

internal sealed record InterceptorIdentity(string Key, string Label);

internal static class InterceptorSymbols
{
#pragma warning disable RSEXPERIMENTAL002
    public static IMethodSymbol? Find(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken cancellationToken) =>
        model.GetInterceptorMethod(invocation, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002

    public static IReadOnlyDictionary<IMethodSymbol, InterceptorIdentity> Index(CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees,
        SymbolNames symbols, Func<IMethodSymbol, string>? scope, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.AttributeLists.Count > 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is not { ContainingType.IsFileLocal: true } method) continue;
                if (method.GetAttributes().Any(a => a.AttributeClass is { Name: "InterceptsLocationAttribute" } type
                    && type.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices")) candidates.Add(SymbolNames.Normalize(method));
            }
        }
        var result = new Dictionary<IMethodSymbol, InterceptorIdentity>(SymbolEqualityComparer.Default);
        if (candidates.Count == 0) return result;
        var anchors = new Dictionary<IMethodSymbol, List<(string Key, string Label)>>(SymbolEqualityComparer.Default);
        foreach (var tree in trees.OrderBy(t => symbols.Path(t.FilePath), StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree);
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol original) continue;
                var owner = model.GetEnclosingSymbol(invocation.SpanStart, cancellationToken);
                while (owner is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction }) owner = owner.ContainingSymbol;
                var ownerKey = owner is IMethodSymbol method ? symbols.Key(method) : owner?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<top-level>";
                var ownerLabel = owner is IMethodSymbol declaringMethod ? symbols.Label(declaringMethod) : owner?.Name ?? "<top-level>";
                var anchor = symbols.Path(tree.FilePath) + "\0" + ownerKey + "\0" + symbols.Key(original) + "\0" + SymbolNames.Compact(invocation);
                ordinals.TryGetValue(anchor, out var ordinal);
                ordinals[anchor] = ++ordinal;
                var interceptor = Find(model, invocation, cancellationToken);
                if (interceptor is null || !candidates.Contains(SymbolNames.Normalize(interceptor))) continue;
                interceptor = SymbolNames.Normalize(interceptor);
                if (!anchors.TryGetValue(interceptor, out var locations)) anchors[interceptor] = locations = [];
                locations.Add((anchor + "\0" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    symbols.Label(original) + " [interceptor in " + ownerLabel + "]"));
            }
        }
        foreach (var (method, locations) in anchors)
        {
            var ordered = locations.OrderBy(location => location.Key, StringComparer.Ordinal).ToArray();
            var identity = string.Join("\0", ordered.Select(location => location.Key));
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            result.Add(method, new InterceptorIdentity((scope?.Invoke(method) ?? "source") + "::<interceptor>:" + digest, ordered[0].Label));
        }
        return result;
    }
}
