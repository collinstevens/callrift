using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Callrift.Core;

public sealed class SourceOnlyAnalysisProvider : IAnalysisProvider
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);
    private const string GlobalUsings = "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;";

    public Task<CallGraph> AnalyzeAsync(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(snapshot, options, cancellationToken), cancellationToken);

    public SyntaxTree[] Parse(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        var classifier = new ProjectClassifier(snapshot);
        var files = snapshot.Files.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && (options.IncludeTests || !classifier.IsTest(f.Path))).ToArray();
        var trees = new SyntaxTree[files.Length];
        Parallel.For(0, files.Length, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            i => trees[i] = CSharpSyntaxTree.ParseText(files[i].Content, ParseOptions, files[i].Path, cancellationToken: cancellationToken));
        return trees;
    }

    public CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create("Callrift.Source",
        trees.Append(CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, "<implicit-usings>")), References.Value,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, concurrentBuild: true));

    private CallGraph Analyze(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken)
    {
        var trees = Parse(snapshot, options, cancellationToken);
        var compilation = CreateCompilation(trees);
        var members = new ConcurrentBag<Member>();
        var types = new ConcurrentBag<INamedTypeSymbol>();
        var diagnostics = new ConcurrentBag<AnalysisDiagnostic>();
        Parallel.ForEach(trees, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, tree =>
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            var collector = new CallCollector(model, diagnostics, cancellationToken);
            foreach (var node in root.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is TypeDeclarationSyntax typeDeclaration && model.GetDeclaredSymbol(typeDeclaration, cancellationToken) is INamedTypeSymbol type)
                    types.Add(type);
                IMethodSymbol? symbol = node switch
                {
                    MethodDeclarationSyntax method => model.GetDeclaredSymbol(method, cancellationToken),
                    ConstructorDeclarationSyntax constructor => model.GetDeclaredSymbol(constructor, cancellationToken),
                    LocalFunctionStatementSyntax local => model.GetDeclaredSymbol(local, cancellationToken),
                    _ => null
                };
                if (symbol is null)
                    continue;
                SyntaxNode? body = node switch
                {
                    MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
                    ConstructorDeclarationSyntax constructor => (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody,
                    LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
                    _ => null
                };
                var calls = new List<CallStep>();
                if (node is ConstructorDeclarationSyntax { Initializer: { } initializer })
                    calls.AddRange(collector.Collect(initializer));
                if (body is not null)
                    calls.AddRange(collector.Collect(body));
                var comparisonBody = body;
                if (node is ConstructorDeclarationSyntax { Initializer: { } constructorInitializer })
                {
                    var statements = new List<StatementSyntax>
                    {
                        SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName(constructorInitializer.ThisOrBaseKeyword.ValueText), constructorInitializer.ArgumentList))
                    };
                    if (body is BlockSyntax block) statements.AddRange(block.Statements);
                    else if (body is ArrowExpressionClauseSyntax arrow) statements.Add(SyntaxFactory.ExpressionStatement(arrow.Expression));
                    comparisonBody = SyntaxFactory.Block(statements);
                }
                members.Add(new Member(SymbolNames.Key(symbol), SymbolNames.Label(symbol), SymbolNames.MatchName(symbol),
                    symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + " -> " + symbol.ReturnType.ToDisplayString(),
                    SymbolNames.Location(node), body is not null, calls)
                { Body = comparisonBody });
            }
            if (root is CompilationUnitSyntax unit && unit.Members.OfType<GlobalStatementSyntax>().Any())
            {
                var key = "source::<top-level>:" + tree.FilePath;
                members.Add(new Member(key, tree.FilePath + "::<top-level>", key, key, SymbolNames.Location(unit.Members.OfType<GlobalStatementSyntax>().First()), true,
                    unit.Members.OfType<GlobalStatementSyntax>().SelectMany(collector.Collect).ToArray())
                { Body = SyntaxFactory.Block(unit.Members.OfType<GlobalStatementSyntax>().Select(s => s.Statement)) });
            }
            foreach (var error in tree.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error))
                diagnostics.Add(new AnalysisDiagnostic(error.Id, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), new SourceLocation(tree.FilePath, error.Location.GetLineSpan().StartLinePosition.Line + 1, 1)));
        });
        var indexed = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (var group in members.OrderBy(m => m.Location.Path, StringComparer.Ordinal).ThenBy(m => m.Location.Line).GroupBy(m => m.Key))
        {
            var bodies = group.Where(m => m.HasBody).ToArray();
            if (bodies.Length > 1)
            {
                diagnostics.Add(new AnalysisDiagnostic("duplicate-member", $"Multiple bodies for {group.Key}; expansion omitted.", group.First().Location));
                indexed[group.Key] = group.First() with { Calls = [], HasBody = false };
            }
            else
                indexed[group.Key] = bodies.FirstOrDefault() ?? group.First();
        }
        var distinctTypes = types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        AddInitializers(distinctTypes, compilation, indexed, diagnostics, cancellationToken);
        var implementations = BuildImplementationMap(distinctTypes, indexed, cancellationToken);
        return new CallGraph(indexed, implementations, diagnostics.Distinct().OrderBy(d => d.Location?.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location?.Line).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray());
    }

    private static void AddInitializers(IEnumerable<INamedTypeSymbol> types, CSharpCompilation compilation, Dictionary<string, Member> members,
        ConcurrentBag<AnalysisDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        foreach (var type in types)
        {
            var declarations = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken)).OfType<TypeDeclarationSyntax>()
                .OrderBy(d => d.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(d => d.SpanStart).ToArray();
            var initializers = declarations.SelectMany(d => d.Members).SelectMany(m => m switch
            {
                FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) && !field.Modifiers.Any(SyntaxKind.ConstKeyword) => field.Declaration.Variables.Select(v => v.Initializer?.Value).OfType<ExpressionSyntax>(),
                PropertyDeclarationSyntax { Initializer: { } initializer } property when !property.Modifiers.Any(SyntaxKind.StaticKeyword) => [initializer.Value],
                _ => Enumerable.Empty<ExpressionSyntax>()
            }).ToArray();
            foreach (var constructor in type.InstanceConstructors)
            {
                var syntax = constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                if (syntax is ConstructorDeclarationSyntax { Initializer.ThisOrBaseKeyword.RawKind: (int)SyntaxKind.ThisKeyword }) continue;
                var key = SymbolNames.Key(constructor);
                members.TryGetValue(key, out var existing);
                if (initializers.Length == 0 && syntax is not TypeDeclarationSyntax) continue;
                var calls = initializers.SelectMany(e => new CallCollector(compilation.GetSemanticModel(e.SyntaxTree), diagnostics, cancellationToken).Collect(e)).ToList();
                var bodyParts = initializers.Select(e => (StatementSyntax)SyntaxFactory.ExpressionStatement(e)).ToList();
                if (syntax is TypeDeclarationSyntax { BaseList: { } bases })
                    foreach (var primaryBase in bases.Types.OfType<PrimaryConstructorBaseTypeSyntax>())
                    {
                        calls.AddRange(new CallCollector(compilation.GetSemanticModel(primaryBase.SyntaxTree), diagnostics, cancellationToken).Collect(primaryBase));
                        foreach (var argument in primaryBase.ArgumentList.Arguments) bodyParts.Add(SyntaxFactory.ExpressionStatement(argument.Expression));
                    }
                if (existing is not null)
                {
                    calls.AddRange(existing.Calls);
                    if (existing.Body is BlockSyntax block) bodyParts.AddRange(block.Statements);
                    else if (existing.Body is ArrowExpressionClauseSyntax arrow) bodyParts.Add(SyntaxFactory.ExpressionStatement(arrow.Expression));
                    members[key] = existing with { Calls = calls, Body = SyntaxFactory.Block(bodyParts) };
                }
                else
                {
                    var locationNode = syntax ?? declarations.First();
                    members[key] = new Member(key, SymbolNames.Label(constructor), SymbolNames.MatchName(constructor), constructor.ToDisplayString(), SymbolNames.Location(locationNode), true, calls)
                    { Body = SyntaxFactory.Block(bodyParts) };
                }
            }
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildImplementationMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members, CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void Add(IMethodSymbol contract, IMethodSymbol implementation)
        {
            var key = SymbolNames.Key(contract);
            var target = SymbolNames.Key(implementation);
            if (key == target || !members.TryGetValue(target, out var member) || !member.HasBody)
                return;
            if (!map.TryGetValue(key, out var targets))
                map[key] = targets = new SortedSet<string>(StringComparer.Ordinal);
            targets.Add(target);
        }
        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.TypeKind == TypeKind.Interface || type.IsAbstract)
                continue;
            foreach (var contract in type.AllInterfaces)
                foreach (var method in contract.GetMembers().OfType<IMethodSymbol>())
                    if (type.FindImplementationForInterfaceMember(method) is IMethodSymbol implementation)
                        Add(method, implementation);
            var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            for (var current = type; current is not null; current = current.BaseType)
                foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
                {
                    if (overridden.Contains(method)) continue;
                    for (var parent = method.OverriddenMethod; parent is not null; parent = parent.OverriddenMethod)
                    {
                        overridden.Add(parent);
                        if (parent.IsAbstract) Add(parent, method);
                    }
                }
        }
        return map.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToArray(), StringComparer.Ordinal);
    }

    public static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        return ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime reference assemblies are unavailable."))
            .Split(Path.PathSeparator).Where(p => Path.GetDirectoryName(p) == runtimeDirectory).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
    }
}
