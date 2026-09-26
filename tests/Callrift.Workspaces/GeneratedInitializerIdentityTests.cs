using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Callrift.Workspaces;

public sealed class GeneratedInitializerIdentityTests
{
    private static readonly (string Name, bool Explicit, bool Shared, string Change)[] Fixtures =
    [
        (Name: "implicit-unchanged", Explicit: false, Shared: false, Change: "none"),
        (Name: "explicit-unchanged", Explicit: true, Shared: false, Change: "none"),
        (Name: "shared-unchanged", Explicit: false, Shared: true, Change: "none"),
        (Name: "implicit-call-change", Explicit: false, Shared: false, Change: "call"),
        (Name: "explicit-call-change", Explicit: true, Shared: false, Change: "call"),
        (Name: "shared-call-change", Explicit: false, Shared: true, Change: "call"),
        (Name: "implicit-literal-change", Explicit: false, Shared: false, Change: "literal"),
        (Name: "shared-literal-change", Explicit: false, Shared: true, Change: "literal")
    ];

    public static IEnumerable<object[]> Cases => Fixtures.SelectMany(fixture => new[] { new object[] { fixture.Name, false }, new object[] { fixture.Name, true } });

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedInitializersKeepStableIdentityAndExposeRealChanges(string name, bool serialized)
    {
        var fixture = Fixtures.Single(fixture => fixture.Name == name);
        var options = new DiffOptions { MaxDepth = 10, IncludeExternals = true };
        var before = Graph("11111111111111111111111111111111", false);
        var after = Graph("22222222222222222222222222222222", true);
        var diff = CallriftService.Compare(before, after, options);
        var treeBefore = CallQueries.Query(before, new QueryRequest("unused") { Options = options with { Entries = ["Flow.EntryA", "Flow.EntryB"] } });
        var treeAfter = CallQueries.Query(after, new QueryRequest("unused") { Options = options with { Entries = ["Flow.EntryA", "Flow.EntryB"] } });
        var keyBefore = Flatten(treeBefore.Trees).Where(node => node.Label.StartsWith("initialization of ", StringComparison.Ordinal)).Select(node => node.After!.SymbolId).Distinct().Order().ToArray();
        var keyAfter = Flatten(treeAfter.Trees).Where(node => node.Label.StartsWith("initialization of ", StringComparison.Ordinal)).Select(node => node.After!.SymbolId).Distinct().Order().ToArray();
        var expectedRoots = fixture.Change == "none" ? Array.Empty<string>() : fixture.Shared ? ["Flow.EntryA", "Flow.EntryB"] : new[] { "Flow.EntryA" };
        var actualRoots = diff.Trees.Select(node => node.Label).Order().ToArray();
        var nodes = Flatten(diff.Trees).ToArray();
        var stableKeys = keyBefore.SequenceEqual(keyAfter) && keyAfter.Length == (fixture.Shared ? 1 : 2);
        var noRandomNames = !JsonRenderer.Render(treeAfter).Contains("22222222222222222222222222222222", StringComparison.Ordinal);
        var changedCalls = fixture.Change != "call" || nodes.Any(node => node.Label == "Sink.SeedBefore" && node.Mark == '-') && nodes.Any(node => node.Label == "Sink.SeedAfter" && node.Mark == '+');
        Assert.True(stableKeys);
        Assert.True(noRandomNames);
        Assert.Equal(expectedRoots, actualRoots);
        Assert.True(changedCalls);
        Assert.Empty(diff.Diagnostics);

        CallGraph Graph(string suffix, bool edited)
        {
            const string flow = "public static class Flow { public static int EntryA() => Original.RunA(); public static int EntryB() => Original.RunB(); } public static class Original { public static int RunA() => 0; public static int RunB() => 0; } public static class Sink { public static int SeedBefore() => 1; public static int SeedAfter() => 2; public static int Other() => 3; }";
            var provider = new SourceOnlyAnalysisProvider();
            var referenceCompilation = provider.CreateCompilation(provider.Parse(new SourceSnapshot("references", []), new AnalysisOptions()));
            var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Generated")]);
            var tree = CSharpSyntaxTree.ParseText(flow, parse, "Flow.cs");
            var compilation = CSharpCompilation.Create("GeneratedFixture", [tree], referenceCompilation.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
#pragma warning disable RSEXPERIMENTAL002
            var attributes = invocations.Select(invocation => model.GetInterceptableLocation(invocation)!.GetInterceptsLocationAttributeSyntax()).ToArray();
#pragma warning restore RSEXPERIMENTAL002
            var initializer = fixture.Change == "literal" ? edited ? "2" : "1" : edited && fixture.Change == "call" ? "Sink.SeedAfter()" : "Sink.SeedBefore()";
            string Declaration(string type, string value, string methods) => fixture.Explicit
                ? $"file static class {type} {{ private static readonly int Value; static {type}() {{ Value = {value}; }} {methods} }}"
                : $"file static class {type} {{ private static readonly int Value = {value}; {methods} }}";
            var methodA = attributes[0] + " public static int InvokeA() => Value;";
            var methodB = attributes[1] + " public static int InvokeB() => Value;";
            var generated = "namespace Generated { " + Declaration("Interceptor_" + suffix, initializer, methodA + (fixture.Shared ? methodB : ""))
                + (fixture.Shared ? "" : Declaration("Other_" + suffix, "Sink.Other()", methodB)) + " } namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)] file sealed class InterceptsLocationAttribute : System.Attribute { public InterceptsLocationAttribute(int version, string data) {} } }";
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, parse, "Generated/Interceptors.g.cs"));
            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors.Select(error => error.ToString())));
            var graph = SourceOnlyAnalysisProvider.AnalyzeCompilation(compilation, scope: _ => "project:App.csproj@net11.0", includeBodyFingerprints: true);
            return serialized ? JsonSerializer.Deserialize<CallGraph>(JsonSerializer.Serialize(graph))! : graph;
        }
    }

    private static IEnumerable<DiffNode> Flatten(IEnumerable<DiffNode> nodes) => nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}
