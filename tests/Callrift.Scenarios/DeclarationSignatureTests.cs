using Callrift.Core;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Callrift.Scenarios;

[Trait("Layer", "Fast")]
public sealed class DeclarationSignatureTests
{
    public static IEnumerable<object[]> Changes()
    {
        yield return Method("access", "private void Run() {}", "public void Run() {}", "private Flow.Run() -> void", "public Flow.Run() -> void");
        yield return Method("protected-internal", "protected void Run() {}", "protected internal void Run() {}", "protected Flow.Run() -> void", "protected internal Flow.Run() -> void");
        yield return Method("private-protected", "private void Run() {}", "private protected void Run() {}", "private Flow.Run() -> void", "private protected Flow.Run() -> void");
        yield return Method("static", "public void Run() {}", "public static void Run() {}", "public Flow.Run() -> void", "public static Flow.Run() -> void");
        yield return Method("virtual", "public void Run() {}", "public virtual void Run() {}", "public Flow.Run() -> void", "public virtual Flow.Run() -> void");
        yield return Method("parameter-name", "public void Run(int before) {}", "public void Run(int after) {}", "public Flow.Run(int before) -> void", "public Flow.Run(int after) -> void");
        yield return Method("escaped-name", "public void Run(int value) {}", "public void Run(int @event) {}", "public Flow.Run(int value) -> void", "public Flow.Run(int @event) -> void");
        yield return Method("default-value", "public void Run(int value = 1) {}", "public void Run(int value = 2) {}", "public Flow.Run([int value = 1]) -> void", "public Flow.Run([int value = 2]) -> void");
        yield return Method("optional", "public void Run(int value) {}", "public void Run(int value = 1) {}", "public Flow.Run(int value) -> void", "public Flow.Run([int value = 1]) -> void");
        yield return Method("optional-attribute", "public void Run(object value) {}", "public void Run([System.Runtime.InteropServices.Optional] object value) {}", "public Flow.Run(object value) -> void", "public Flow.Run([object value]) -> void");
        yield return Method("nullable-parameter", "public void Run(string value) {}", "public void Run(string? value) {}", "public Flow.Run(string value) -> void", "public Flow.Run(string? value) -> void");
        yield return Method("nested-nullability", "public void Run(System.Collections.Generic.List<string> value) {}", "public void Run(System.Collections.Generic.List<string?> value) {}", "public Flow.Run(System.Collections.Generic.List<string> value) -> void", "public Flow.Run(System.Collections.Generic.List<string?> value) -> void");
        yield return Method("nullable-return", "public string Run() => throw new System.Exception();", "public string? Run() => throw new System.Exception();", "public Flow.Run() -> string", "public Flow.Run() -> string?");
        yield return Method("params", "public void Run(int[] values) {}", "public void Run(params int[] values) {}", "public Flow.Run(int[] values) -> void", "public Flow.Run(params int[] values) -> void");
        yield return Method("scoped-value", "public void Run(System.Span<int> value) {}", "public void Run(scoped System.Span<int> value) {}", "public Flow.Run(System.Span<int> value) -> void", "public Flow.Run(scoped System.Span<int> value) -> void");
        yield return Method("scoped-ref", "public void Run(ref int value) {}", "public void Run(scoped ref int value) {}", "public Flow.Run(ref int value) -> void", "public Flow.Run(scoped ref int value) -> void");
        yield return Method("ref-return", "public ref int Run() => throw new System.Exception();", "public ref readonly int Run() => throw new System.Exception();", "public Flow.Run() -> ref int", "public Flow.Run() -> ref readonly int");
        yield return Method("nullable-ref-return", "public ref string Run() => throw new System.Exception();", "public ref string? Run() => throw new System.Exception();", "public Flow.Run() -> ref string", "public Flow.Run() -> ref string?");
        yield return Method("async", "public System.Threading.Tasks.Task Run() { throw new System.Exception(); }", "public async System.Threading.Tasks.Task Run() { throw new System.Exception(); }", "public Flow.Run() -> System.Threading.Tasks.Task", "public async Flow.Run() -> System.Threading.Tasks.Task");
        yield return Method("class-constraint", "public void Run<T>() {}", "public void Run<T>() where T : class {}", "public Flow.Run<T>() -> void", "public Flow.Run<T>() -> void where T : class");
        yield return Method("nullable-constraint", "public void Run<T>() where T : class {}", "public void Run<T>() where T : class? {}", "public Flow.Run<T>() -> void where T : class", "public Flow.Run<T>() -> void where T : class?");
        yield return Method("value-constraint", "public void Run<T>() {}", "public void Run<T>() where T : struct {}", "public Flow.Run<T>() -> void", "public Flow.Run<T>() -> void where T : struct");
        yield return Method("unmanaged-constraint", "public void Run<T>() where T : struct {}", "public void Run<T>() where T : unmanaged {}", "public Flow.Run<T>() -> void where T : struct", "public Flow.Run<T>() -> void where T : unmanaged");
        yield return Method("notnull-constraint", "public void Run<T>() {}", "public void Run<T>() where T : notnull {}", "public Flow.Run<T>() -> void", "public Flow.Run<T>() -> void where T : notnull");
        yield return Method("constructor-constraint", "public void Run<T>() where T : class {}", "public void Run<T>() where T : class, new() {}", "public Flow.Run<T>() -> void where T : class", "public Flow.Run<T>() -> void where T : class, new()");
        yield return Method("interface-constraint", "public void Run<T>() {}", "public void Run<T>() where T : System.IDisposable {}", "public Flow.Run<T>() -> void", "public Flow.Run<T>() -> void where T : System.IDisposable");
        yield return Method("ref-struct-constraint", "public void Run<T>() {}", "public void Run<T>() where T : allows ref struct {}", "public Flow.Run<T>() -> void", "public Flow.Run<T>() -> void where T : allows ref struct");
        yield return Method("parameter-constraint", "public void Run<T, U>() {}", "public void Run<T, U>() where T : U {}", "public Flow.Run<T, U>() -> void", "public Flow.Run<T, U>() -> void where T : U");
        yield return ["extension", "static class Flow { public static void Run(string value) {} }", "static class Flow { public static void Run(this string value) {} }", "Flow.Run", "public static Flow.Run(string value) -> void", "public static Flow.Run(this string value) -> void"];
        yield return ["readonly", "struct Flow { public void Run() {} }", "struct Flow { public readonly void Run() {} }", "Flow.Run", "public Flow.Run() -> void", "public readonly Flow.Run() -> void"];
        yield return ["interface-static", "interface Flow { void Run(); }", "interface Flow { static abstract void Run(); }", "Flow.Run", "public abstract Flow.Run() -> void", "public static abstract Flow.Run() -> void"];
        yield return ["interface-virtual", "interface Flow { static void Run() {} }", "interface Flow { static virtual void Run() {} }", "Flow.Run", "public static Flow.Run() -> void", "public static virtual Flow.Run() -> void"];
        yield return ["sealed-override", "class Base { public virtual void Run() {} } class Flow : Base { public override void Run() {} }", "class Base { public virtual void Run() {} } class Flow : Base { public sealed override void Run() {} }", "Flow.Run", "public override Flow.Run() -> void", "public sealed override Flow.Run() -> void"];
        yield return ["local-static", "class Flow { public void Owner() { void Run() {} } }", "class Flow { public void Owner() { static void Run() {} } }", "Flow.Owner.Run", "Run() -> void", "static Run() -> void"];
        yield return ["partial", "partial class Flow { private void Run() {} }", "partial class Flow { private partial void Run(); private partial void Run() {} }", "Flow.Run", "private Flow.Run() -> void", "private partial Flow.Run() -> void"];
        yield return ["type-constraint", "class Flow<T> { public void Run() {} }", "class Flow<T> where T : class { public void Run() {} }", "Flow<T>.Run", "public Flow<T>.Run() -> void", "public Flow<T>.Run() -> void [Flow<T> where T : class]"];
        yield return ["outer-type-constraint", "class Outer<T> { public class Flow<U> { public void Run() {} } }", "class Outer<T> where T : class { public class Flow<U> { public void Run() {} } }", "Outer<T>.Flow<U>.Run", "public Outer<T>.Flow<U>.Run() -> void", "public Outer<T>.Flow<U>.Run() -> void [Outer<T> where T : class]"];
        yield return ["outer-method-constraint", "class Flow { public void Owner<T>() { void Run() {} } }", "class Flow { public void Owner<T>() where T : class { void Run() {} } }", "Flow.Owner.Run", "Run() -> void", "Run() -> void [Flow.Owner<T>() where T : class]"];
        yield return ["constructor", "class Flow { private Flow(int value = 1) {} }", "class Flow { public Flow(int value = 2) {} }", "new Flow", "private Flow.Flow([int value = 1]) -> void", "public Flow.Flow([int value = 2]) -> void"];
        yield return ["nullable-base-constraint", "class Base {} class Flow { public void Run<T>() where T : Base {} }", "class Base {} class Flow { public void Run<T>() where T : Base? {} }", "Flow.Run", "public Flow.Run<T>() -> void where T : Base", "public Flow.Run<T>() -> void where T : Base?"];
        yield return ["default-constraint", "class Base { public virtual void Run<T>(T value) {} } class Flow : Base { public override void Run<T>(T value) {} }", "class Base { public virtual void Run<T>(T value) {} } class Flow : Base { public override void Run<T>(T value) where T : default {} }", "Flow.Run", "public override Flow.Run<T>(T value) -> void", "public override Flow.Run<T>(T value) -> void where T : default"];
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public void DeclarationChangesRetainIdentity(string name, string before, string after, string label, string beforeSignature, string afterSignature)
    {
        var left = Analyze(before, name);
        var right = Analyze(after, name);
        var beforeMember = Assert.Single(left.Members.Values, member => member.Label == label);
        var afterMember = Assert.Single(right.Members.Values, member => member.Label == label);
        Assert.Equal(beforeMember.Key, afterMember.Key);
        Assert.Equal(beforeSignature, beforeMember.Signature);
        Assert.Equal(afterSignature, afterMember.Signature);
        Assert.NotEqual(beforeMember.Signature, afterMember.Signature);
        if (beforeMember.HasBody && afterMember.HasBody)
        {
            var result = CallriftService.Compare(left, right, new DiffOptions { Entries = [label] });
            var root = Assert.Single(result.Trees);
            Assert.Equal('~', root.Mark);
            Assert.Equal("signature changed", root.Detail);
            Assert.Equal(beforeMember.Key, root.Before?.SymbolId);
            Assert.Equal(afterMember.Key, root.After?.SymbolId);
        }
    }

    public static IEnumerable<object[]> EquivalentDeclarations()
    {
        yield return ["implicit-private", "class Flow { void Run() {} }", "class Flow { private void Run() {} }"];
        yield return ["implicit-interface-access", "interface Flow { void Run(); }", "interface Flow { public abstract void Run(); }"];
        yield return ["constant-expression", "class Flow { public void Run(int value = 1 + 1) {} }", "class Flow { public void Run(int value = 2) {} }"];
        yield return ["constraint-order", "class Flow { public void Run<T, U>() where U : class where T : System.IDisposable, System.ICloneable, new() {} }", "class Flow { public void Run<T, U>() where T : System.ICloneable, System.IDisposable, new() where U : class {} }"];
        yield return ["constraint-alias", "using Alias = System.IDisposable; class Flow { public void Run<T>() where T : Alias {} }", "class Flow { public void Run<T>() where T : global::System.IDisposable {} }"];
        yield return ["implicit-readonly", "readonly struct Flow { public void Run() {} }", "readonly struct Flow { public readonly void Run() {} }"];
        yield return ["base-before-interfaces", "class ZBase {} interface IAlpha {} interface IBeta {} class Flow { public void Run<T>() where T : ZBase, IBeta, IAlpha, new() {} }", "class ZBase {} interface IAlpha {} interface IBeta {} class Flow { public void Run<T>() where T : ZBase, IAlpha, IBeta, new() {} }"];
    }

    [Theory]
    [MemberData(nameof(EquivalentDeclarations))]
    public void EquivalentDeclarationsDoNotInventChanges(string name, string before, string after)
    {
        var left = Analyze(before, name);
        var right = Analyze(after, name);
        Assert.Equal(left.Members.OrderBy(member => member.Key).Select(member => (member.Key, member.Value.Signature)),
            right.Members.OrderBy(member => member.Key).Select(member => (member.Key, member.Value.Signature)));
        Assert.Empty(CallriftService.Compare(left, right, new DiffOptions()).Trees);
    }

    private static object[] Method(string name, string before, string after, string beforeSignature, string afterSignature) =>
        [name, "class Flow { " + before + " }", "class Flow { " + after + " }", "Flow.Run", beforeSignature, afterSignature];

    private static CallGraph Analyze(string source, string name)
    {
        source = "#nullable enable\n" + source;
        var provider = new SourceOnlyAnalysisProvider();
        var trees = provider.Parse(new SourceSnapshot(name, [new SourceFile(name + ".cs", source, source)]), new AnalysisOptions());
        var compilation = provider.CreateCompilation(trees);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var graph = SourceOnlyAnalysisProvider.AnalyzeCompilation(compilation);
        Assert.Empty(graph.Diagnostics);
        return graph;
    }
}
