using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.RealWorldCases;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class FloorBenchmarks
{
    private readonly SourceOnlyAnalysisProvider provider = new();
    private GitRepository repository = null!;
    private RealWorldCase entry = null!;
    private IReadOnlyList<GitEntry> entries = null!;
    private SourceSnapshot before = null!;
    private SourceSnapshot after = null!;
    private SyntaxTree[] trees = null!;
    private CSharpCompilation compilation = null!;
    private CallGraph oldGraph = null!;
    private CallGraph newGraph = null!;
    private HashSet<string> changed = null!;
    private IReadOnlyList<CallTree> oldTrees = null!;
    private IReadOnlyList<CallTree> newTrees = null!;
    private DiffResult diff = null!;
    private readonly DiffOptions options = new() { Entries = ["MessageTemplateParser.ParsePropertyToken"] };

    [GlobalSetup]
    public async Task Setup()
    {
        entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == "serilog-alignment-guard");
        repository = await GitRepository.OpenAsync(await RealWorldCaseStore.PrepareAsync(entry));
        entries = await repository.ListEntriesAsync(entry.After);
        before = await repository.ReadSnapshotAsync(entry.Before);
        after = await repository.ReadSnapshotAsync(entry.After);
        trees = provider.Parse(after, new AnalysisOptions());
        compilation = provider.CreateCompilation(trees);
        oldGraph = await provider.AnalyzeAsync(before, new AnalysisOptions());
        newGraph = await provider.AnalyzeAsync(after, new AnalysisOptions());
        changed = ChangeDetector.FindChanges(oldGraph, newGraph);
        var roots = EntrySelector.Select(oldGraph, newGraph, changed, options);
        oldTrees = roots.Where(oldGraph.Members.ContainsKey).Select(new TreeExpander(oldGraph, changed, options).Expand).ToArray();
        newTrees = roots.Where(newGraph.Members.ContainsKey).Select(new TreeExpander(newGraph, changed, options).Expand).ToArray();
        diff = CallriftService.Compare(oldGraph, newGraph, options);
    }

    [Benchmark]
    public Task<IReadOnlyList<GitEntry>> ListPaths() => repository.ListEntriesAsync(entry.After);

    [Benchmark]
    public Task<IReadOnlyList<SourceFile>> ReadBlobs() => repository.ReadBlobsAsync(entries);

    [Benchmark]
    public SyntaxTree[] ParseFiles() => provider.Parse(after, new AnalysisOptions());

    [Benchmark]
    public IReadOnlyList<MetadataReference> LoadReferences() => SourceOnlyAnalysisProvider.LoadReferences();

    [Benchmark]
    public CSharpCompilation CreateCompilation() => provider.CreateCompilation(trees);

    [IterationSetup(Target = nameof(BindInvocations))]
    public void ResetCompilation() => compilation = provider.CreateCompilation(trees);

    [Benchmark]
    public int BindInvocations()
    {
        var resolved = 0;
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                if (model.GetSymbolInfo(invocation).Symbol is not null) resolved++;
        }
        return resolved;
    }

    [Benchmark]
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ImplementationMap()
    {
        var types = trees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Select(n => compilation.GetSemanticModel(t).GetDeclaredSymbol(n)).OfType<INamedTypeSymbol>());
        return SourceOnlyAnalysisProvider.BuildImplementationMap(types, newGraph.Members);
    }

    [Benchmark]
    public HashSet<string> Equivalence() => ChangeDetector.FindChanges(oldGraph, newGraph);

    [Benchmark]
    public CallTree[] ExpandTrees() => EntrySelector.Select(oldGraph, newGraph, changed, options)
        .Where(newGraph.Members.ContainsKey).Select(new TreeExpander(newGraph, changed, options).Expand).ToArray();

    [Benchmark]
    public IReadOnlyList<DiffNode> AlignTrees() => TreeDiffer.Compare(oldTrees, newTrees);

    [Benchmark]
    public string Render() => DiffRenderer.Render(diff, options);

    [Benchmark]
    public string RenderJson() => JsonRenderer.Render(diff);

    [Benchmark]
    public DiffResult QueryTree() => CallQueries.Query(newGraph, new QueryRequest(repository.Root) { Options = options });

    [Benchmark]
    public DiffResult QueryReach() => CallQueries.Query(newGraph, new QueryRequest(repository.Root) { Options = options, Target = "new TextToken" });

    [Benchmark]
    public Task<DiffResult> Diff() => new CallriftService().DiffAsync(new DiffRequest(repository.Root, entry.Before, entry.After) { Options = options });
}
