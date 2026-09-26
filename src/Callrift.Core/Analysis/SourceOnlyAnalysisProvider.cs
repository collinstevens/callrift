using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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

    public SyntaxTree[] Parse(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default) =>
        Parse(snapshot, options, new ProjectClassifier(snapshot), cancellationToken);

    private static SyntaxTree[] Parse(SourceSnapshot snapshot, AnalysisOptions options, ProjectClassifier classifier, CancellationToken cancellationToken)
    {
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
        var classifier = new ProjectClassifier(snapshot);
        var trees = Parse(snapshot, options, classifier, cancellationToken);
        var compilation = CreateCompilation(trees);
        var graph = AnalyzeCompilation(compilation, trees, cancellationToken: cancellationToken);
        return options.IncludeTests ? graph : graph with
        {
            Diagnostics = graph.Diagnostics.Concat(classifier.Diagnostics).OrderBy(d => d.Location?.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Location?.Line).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray()
        };
    }

    public static CallGraph AnalyzeCompilation(CSharpCompilation compilation, IEnumerable<SyntaxTree>? syntaxTrees = null,
        Func<IMethodSymbol, string>? scope = null, Func<string, string>? logicalPath = null, bool includeBodyFingerprints = false,
        CancellationToken cancellationToken = default)
    {
        var trees = (syntaxTrees ?? compilation.SyntaxTrees).ToArray();
        var symbols = new SymbolNames(scope, logicalPath);
        symbols = new SymbolNames(scope, logicalPath, InterceptorSymbols.Index(compilation, trees, symbols, scope, cancellationToken));
        var members = new ConcurrentBag<Member>();
        var types = new ConcurrentBag<INamedTypeSymbol>();
        var diagnostics = new ConcurrentBag<AnalysisDiagnostic>();
        var dispatchTypes = new ConcurrentBag<ITypeSymbol>();
        Parallel.ForEach(trees, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, tree =>
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            var collector = new CallCollector(model, symbols, diagnostics, dispatchTypes, cancellationToken);
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
                members.Add(new Member(symbols.Key(symbol), symbols.Label(symbol), symbols.MatchName(symbol), symbols.Signature(symbol),
                    symbols.Location(node), body is not null, calls)
                {
                    Body = comparisonBody,
                    InstanceType = GenericBindings.HasContainingInstance(symbol) ? DispatchType.From(symbol.ContainingType) : null,
                    GenericParameters = GenericBindings.FromMethod(symbol).Keys.Order(StringComparer.Ordinal).ToArray(),
                    MethodParameters = symbol.TypeParameters.Select(p => DispatchType.From(p).Name).ToArray()
                });
            }
            if (root is CompilationUnitSyntax unit && unit.Members.OfType<GlobalStatementSyntax>().Any())
            {
                var prefix = scope is not null && compilation.GetEntryPoint(cancellationToken) is { } entry ? scope(entry) : "source";
                var key = prefix + "::<top-level>:" + symbols.Path(tree.FilePath);
                members.Add(new Member(key, symbols.Path(tree.FilePath) + "::<top-level>", key, key, symbols.Location(unit.Members.OfType<GlobalStatementSyntax>().First()), true,
                    unit.Members.OfType<GlobalStatementSyntax>().SelectMany(collector.Collect).ToArray())
                { Body = SyntaxFactory.Block(unit.Members.OfType<GlobalStatementSyntax>().Select(s => s.Statement)) });
            }
            foreach (var error in tree.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error))
                diagnostics.Add(new AnalysisDiagnostic(error.Id, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), new SourceLocation(symbols.Path(tree.FilePath), error.Location.GetLineSpan().StartLinePosition.Line + 1, 1)));
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
        AddInitializers(distinctTypes, compilation, indexed, symbols, diagnostics, dispatchTypes, cancellationToken);
        AddRecordClones(distinctTypes, compilation, indexed, symbols, diagnostics, dispatchTypes, cancellationToken);
        var dispatch = BuildDispatchMap(distinctTypes, indexed, symbols, cancellationToken, dispatchTypes);
        if (includeBodyFingerprints)
            foreach (var key in indexed.Keys.ToArray())
            {
                var body = indexed[key].Body;
                indexed[key] = indexed[key] with
                {
                    BodyFingerprint = body is null ? null : Convert.ToHexStringLower(SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Join("\0", body.DescendantTokens().Select(t => t.RawKind + ":" + t.Text)))))
                };
            }
        return new CallGraph(indexed, dispatch.Implementations, diagnostics.Distinct().OrderBy(d => d.Location?.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location?.Line).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray())
        { DispatchContracts = dispatch.Contracts, TypeDefinitions = dispatch.TypeDefinitions };
    }

    private static void AddRecordClones(IEnumerable<INamedTypeSymbol> types, CSharpCompilation compilation, Dictionary<string, Member> members, SymbolNames symbols,
        ConcurrentBag<AnalysisDiagnostic> diagnostics, ConcurrentBag<ITypeSymbol> dispatchTypes, CancellationToken cancellationToken)
    {
        foreach (var type in types.Where(type => type is { IsRecord: true, TypeKind: TypeKind.Class }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clone = type.GetMembers("<Clone>$").OfType<IMethodSymbol>().SingleOrDefault(method => method.IsImplicitlyDeclared);
            if (clone is null) continue;
            var copies = type.InstanceConstructors.Where(ConstructorBindings.IsCopy).ToArray();
            var declarations = type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken))
                .OrderBy(node => node.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(node => node.SpanStart).ToArray();
            var declaration = declarations[0];
            var validDeclarations = declarations.Length == 1 || declarations.All(node => node is RecordDeclarationSyntax record && record.Modifiers.Any(SyntaxKind.PartialKeyword));
            var validCopyAccessibility = copies.Length == 1 && (type.IsSealed || copies[0].DeclaredAccessibility is Accessibility.Public or Accessibility.Protected);
            var hasBody = !clone.IsAbstract && validDeclarations && validCopyAccessibility;
            if (!clone.IsAbstract && copies.Length != 1)
                diagnostics.Add(new AnalysisDiagnostic("unresolved-record-copy", $"Cannot bind a unique copy constructor for {symbols.Label(clone)}; expansion omitted.", symbols.Location(declaration)));
            if (!validDeclarations)
                diagnostics.Add(new AnalysisDiagnostic("unresolved-record-copy", $"Multiple declarations for {symbols.Label(clone)} must all be partial records; expansion omitted.", symbols.Location(declaration)));
            if (copies.Length == 1 && !validCopyAccessibility)
                diagnostics.Add(new AnalysisDiagnostic("unresolved-record-copy", $"Copy constructor for {symbols.Label(clone)} must be public or protected because the record is not sealed; expansion omitted.", symbols.Location(declaration)));
            if (!validDeclarations || !validCopyAccessibility)
                foreach (var copy in copies)
                    if (members.TryGetValue(symbols.Key(copy), out var member))
                        members[symbols.Key(copy)] = member with { HasBody = false, Calls = [], Body = null };
            CallStep[] calls = hasBody
                ? [new CallCollector(compilation.GetSemanticModel(declaration.SyntaxTree), symbols, diagnostics, dispatchTypes, cancellationToken)
                    .ImplicitConstructor(declaration, copies[0]) with { UsesContainingInstance = false, InvocationReceiverExact = true }]
                : [];
            var key = symbols.Key(clone);
            members[key] = new Member(key, symbols.Label(clone), symbols.MatchName(clone), symbols.Signature(clone), symbols.Location(declaration), hasBody, calls)
            {
                Body = hasBody ? SyntaxFactory.Block() : null,
                InstanceType = DispatchType.From(type),
                GenericParameters = GenericBindings.FromMethod(clone).Keys.Order(StringComparer.Ordinal).ToArray(),
                MethodParameters = []
            };
        }
    }

    private static void AddInitializers(IEnumerable<INamedTypeSymbol> types, CSharpCompilation compilation, Dictionary<string, Member> members, SymbolNames symbols,
        ConcurrentBag<AnalysisDiagnostic> diagnostics, ConcurrentBag<ITypeSymbol> dispatchTypes, CancellationToken cancellationToken)
    {
        var implicitBases = ConstructorBindings.Create(compilation, types, cancellationToken);
        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declarations = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken)).OfType<TypeDeclarationSyntax>()
                .OrderBy(d => d.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(d => d.SpanStart).ToArray();
            var initializers = declarations.SelectMany(d => d.Members).SelectMany(m => m switch
            {
                FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) && !field.Modifiers.Any(SyntaxKind.ConstKeyword) => field.Declaration.Variables.Select(v => v.Initializer?.Value).OfType<ExpressionSyntax>(),
                EventFieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) => field.Declaration.Variables.Select(v => v.Initializer?.Value).OfType<ExpressionSyntax>(),
                PropertyDeclarationSyntax { Initializer: { } initializer } property when !property.Modifiers.Any(SyntaxKind.StaticKeyword) => [initializer.Value],
                _ => Enumerable.Empty<ExpressionSyntax>()
            }).ToArray();
            foreach (var constructor in type.InstanceConstructors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var implementation = constructor.PartialImplementationPart ?? constructor;
                if (implementation.IsExtern) continue;
                var syntax = implementation.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                if (syntax is ConstructorDeclarationSyntax { Initializer.ThisOrBaseKeyword.RawKind: (int)SyntaxKind.ThisKeyword }) continue;
                var key = symbols.Key(constructor);
                members.TryGetValue(key, out var existing);
                if (existing is { HasBody: false }) continue;
                var locationNode = syntax ?? declarations.First();
                var selectedInitializers = ConstructorBindings.IsCopy(constructor) || type.TypeKind == TypeKind.Struct && constructor.IsImplicitlyDeclared ? [] : initializers;
                var calls = selectedInitializers.SelectMany(e => new CallCollector(compilation.GetSemanticModel(e.SyntaxTree), symbols, diagnostics, dispatchTypes, cancellationToken).Collect(e)).ToList();
                var bodyParts = selectedInitializers.Select(e => (StatementSyntax)SyntaxFactory.ExpressionStatement(e)).ToList();
                var explicitBase = syntax is ConstructorDeclarationSyntax { Initializer: not null };
                if (syntax is TypeDeclarationSyntax { BaseList: { } bases })
                    foreach (var primaryBase in bases.Types.OfType<PrimaryConstructorBaseTypeSyntax>())
                    {
                        explicitBase = true;
                        calls.AddRange(new CallCollector(compilation.GetSemanticModel(primaryBase.SyntaxTree), symbols, diagnostics, dispatchTypes, cancellationToken).Collect(primaryBase));
                        bodyParts.Add(SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("base"), primaryBase.ArgumentList)));
                    }
                if (!explicitBase && type.TypeKind == TypeKind.Class && type.BaseType is not null)
                {
                    var model = compilation.GetSemanticModel(locationNode.SyntaxTree);
                    var target = implicitBases.GetValueOrDefault(constructor) ?? (syntax is null ? null : ConstructorBindings.Initializer(model, syntax, cancellationToken));
                    if (target is not null)
                        calls.Add(new CallCollector(model, symbols, diagnostics, dispatchTypes, cancellationToken).ImplicitConstructor(locationNode, target));
                    else
                    {
                        diagnostics.Add(new AnalysisDiagnostic("unresolved-call", $"Cannot bind implicit base constructor for {symbols.Label(constructor)}.", symbols.Location(locationNode)));
                        calls.Add(new CallStep("unresolved", "?base:" + key, "? base()", false, symbols.Location(locationNode), []));
                    }
                    bodyParts.Add(SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("base"), SyntaxFactory.ArgumentList())));
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
                    members[key] = new Member(key, symbols.Label(constructor), symbols.MatchName(constructor), symbols.Signature(constructor), symbols.Location(locationNode), true, calls)
                    {
                        Body = SyntaxFactory.Block(bodyParts),
                        InstanceType = constructor.IsStatic ? null : DispatchType.From(constructor.ContainingType),
                        GenericParameters = GenericBindings.FromMethod(constructor).Keys.Order(StringComparer.Ordinal).ToArray()
                    };
                }
            }
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildImplementationMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members, CancellationToken cancellationToken = default)
        => BuildDispatchMap(types, members, new SymbolNames(), cancellationToken).Implementations;

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildImplementationMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members,
        Func<IMethodSymbol, string> scope, CancellationToken cancellationToken = default)
        => BuildDispatchMap(types, members, new SymbolNames(scope), cancellationToken).Implementations;

    public static DispatchMap BuildDispatchMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members,
        Func<IMethodSymbol, string> scope, CancellationToken cancellationToken = default)
        => BuildDispatchMap(types, members, new SymbolNames(scope), cancellationToken);

    public static DispatchMap BuildDispatchMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members,
        Func<IMethodSymbol, string> scope, Func<IMethodSymbol, string, string> declaringPath, CancellationToken cancellationToken = default)
        => BuildDispatchMap(types, members, new SymbolNames(scope, declaringPath: declaringPath), cancellationToken);

    public static DispatchMap BuildDispatchMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members,
        Func<IMethodSymbol, string> scope, Func<IMethodSymbol, string, string> declaringPath,
        Func<INamedTypeSymbol, string, string> typeDeclaringPath, CancellationToken cancellationToken = default)
        => BuildDispatchMap(types, members, new SymbolNames(scope, declaringPath: declaringPath, typeDeclaringPath: typeDeclaringPath), cancellationToken);

    private static DispatchMap BuildDispatchMap(IEnumerable<INamedTypeSymbol> types, IReadOnlyDictionary<string, Member> members, SymbolNames symbols,
        CancellationToken cancellationToken, IEnumerable<ITypeSymbol>? observedTypes = null)
    {
        var declaredTypes = types.ToArray();
        var map = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var contracts = new Dictionary<string, List<DispatchContract>>(StringComparer.Ordinal);
        void Add(IMethodSymbol contract, IMethodSymbol implementation, IReadOnlyList<DispatchType> receiverTypes, INamedTypeSymbol instanceType, bool includeSelf = false)
        {
            var key = symbols.Key(contract);
            var target = symbols.Key(implementation);
            if ((!includeSelf && key == target) || !members.TryGetValue(target, out var member) || !member.HasBody)
                return;
            if (!map.TryGetValue(key, out var targets))
                map[key] = targets = new SortedSet<string>(StringComparer.Ordinal);
            targets.Add(target);
            if (!contracts.TryGetValue(key, out var candidates)) contracts[key] = candidates = [];
            candidates.Add(new DispatchContract(target, DispatchType.From(contract.ContainingType), receiverTypes)
            { GenericArguments = GenericBindings.FromMethod(implementation), ImplementationType = DispatchType.From(instanceType), ImplementationTypeExact = !instanceType.IsAbstract });
        }
        foreach (var type in declaredTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.TypeKind == TypeKind.Interface)
                continue;
            var receiverTypes = type.AllInterfaces.Select(DispatchType.From).ToList();
            for (var current = type; current is not null; current = current.BaseType) receiverTypes.Add(DispatchType.From(current));
            foreach (var contract in type.AllInterfaces)
                foreach (var method in contract.GetMembers().OfType<IMethodSymbol>())
                    if (type.FindImplementationForInterfaceMember(method) is IMethodSymbol implementation)
                    {
                        var resolved = implementation;
                        for (var current = type; current is not null; current = current.BaseType)
                        {
                            var candidate = current.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => Overrides(m, implementation));
                            if (candidate is null) continue;
                            resolved = candidate;
                            break;
                        }
                        Add(method, resolved, receiverTypes, type);
                    }
            if (type.IsAbstract)
                continue;
            var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            for (var current = type; current is not null; current = current.BaseType)
                foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
                {
                    if (overridden.Contains(method)) continue;
                    if (method.IsVirtual || method.IsOverride) Add(method, method, receiverTypes, type, includeSelf: true);
                    for (var parent = method.OverriddenMethod; parent is not null; parent = parent.OverriddenMethod)
                    {
                        overridden.Add(parent);
                        Add(parent, method, receiverTypes, type);
                    }
                }
        }
        var implementations = map.Where(p => p.Value.Any(target => target != p.Key))
            .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToArray(), StringComparer.Ordinal);
        return new DispatchMap(implementations, contracts
            .ToDictionary(p => p.Key, p => (IReadOnlyList<DispatchContract>)p.Value.OrderBy(c => c.Target, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal))
        { TypeDefinitions = DispatchTypeCatalog.Create(declaredTypes.Concat(observedTypes ?? []), cancellationToken, symbols.TypeIdentity) };
    }

    private static bool Overrides(IMethodSymbol method, IMethodSymbol ancestor)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, ancestor.OriginalDefinition)) return true;
        return false;
    }

    public static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        return ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime reference assemblies are unavailable."))
            .Split(Path.PathSeparator).Where(p => Path.GetDirectoryName(p) == runtimeDirectory).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
    }
}
