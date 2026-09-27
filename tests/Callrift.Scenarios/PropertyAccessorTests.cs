using System.Runtime.Loader;
using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class PropertyAccessorTests
{
    private static readonly (string Name, string Declaration, string ReceiverType, string Invoke, string Runtime)[] Examples =
    [
        ("getter", "class Value { public int Item { get { return Sink.Before(); } set { Sink.Unrelated(); } } }", "Value", "_ = Receiver().Item;", "receiver,before"),
        ("setter", "class Value { public int Item { get { return Sink.Unrelated(); } set { Sink.Before(); } } }", "Value", "Receiver().Item = Sink.Argument();", "receiver,argument,before"),
        ("expression-property", "class Value { public int Item => Sink.Before(); }", "Value", "_ = Receiver().Item;", "receiver,before"),
        ("expression-accessor", "class Value { public int Item { get => Sink.Before(); } }", "Value", "_ = Receiver().Item;", "receiver,before"),
        ("indexer-get", "class Value { public int this[int index] { get => Sink.Before(); set => Sink.Unrelated(); } }", "Value", "_ = Receiver()[Sink.Index()];", "receiver,index,before"),
        ("indexer-set", "class Value { public int this[int index] { get => Sink.Unrelated(); set => Sink.Before(); } }", "Value", "Receiver()[Sink.Index()] = Sink.Argument();", "receiver,index,argument,before"),
        ("expression-indexer", "class Value { public int this[int index] => Sink.Before(); }", "Value", "_ = Receiver()[Sink.Index()];", "receiver,index,before"),
        ("compound", "class Value { public int Item { get => Sink.Getter(); set => Sink.Before(); } }", "Value", "Receiver().Item += Sink.Argument();", "receiver,getter,argument,before"),
        ("indexer-compound", "class Value { public int this[int index] { get => Sink.Getter(); set => Sink.Before(); } }", "Value", "Receiver()[Sink.Index()] += Sink.Argument();", "receiver,index,getter,argument,before"),
        ("prefix", "class Value { public int Item { get => Sink.Getter(); set => Sink.Before(); } }", "Value", "++Receiver().Item;", "receiver,getter,before"),
        ("postfix-indexer", "class Value { public int this[int index] { get => Sink.Getter(); set => Sink.Before(); } }", "Value", "Receiver()[Sink.Index()]++;", "receiver,index,getter,before"),
        ("coalesce", "class Value { public int? Item { get { Sink.Getter(); return null; } set => Sink.Before(); } }", "Value", "Receiver().Item ??= Sink.Argument();", "receiver,getter,argument,before"),
        ("ref-return", "class Value { int storage; public ref int Item { get { Sink.Before(); return ref storage; } } }", "Value", "Receiver().Item = Sink.Argument();", "receiver,before,argument"),
        ("readonly-ref", "class Value { int storage; public ref readonly int Item { get { Sink.Before(); return ref storage; } } }", "Value", "_ = Receiver().Item;", "receiver,before"),
        ("interface", "interface IValue { int Item { get; } } class Value : IValue { public int Item => Sink.Before(); }", "IValue", "_ = Receiver().Item;", "receiver,before"),
        ("explicit-interface", "interface IValue { int this[int index] { get; } } class Value : IValue { int IValue.this[int index] => Sink.Before(); }", "IValue", "_ = Receiver()[Sink.Index()];", "receiver,index,before"),
        ("virtual", "class Base { public virtual int Item => 0; } class Value : Base { public override int Item => Sink.Before(); }", "Base", "_ = Receiver().Item;", "receiver,before"),
        ("base", "class Base { public virtual int Item => Sink.Before(); } class Value : Base { public override int Item => Sink.Unrelated(); public int Read() => base.Item; }", "Value", "_ = Receiver().Read();", "receiver,before"),
        ("default-interface", "interface IValue { int Item => Sink.Before(); } class Value : IValue {}", "IValue", "_ = Receiver().Item;", "receiver,before"),
        ("static-get", "class Value { static Value() { Sink.Initialize(); } public static int Item => Sink.Before(); }", "Value", "_ = Value.Item;", "initialize,before"),
        ("static-set", "class Value { static Value() { Sink.Initialize(); } public static int Item { set => Sink.Before(); } }", "Value", "Value.Item = Sink.Argument();", "argument,initialize,before"),
        ("struct", "struct Value { static Value() { Sink.Initialize(); } public int Item => Sink.Before(); }", "Value", "_ = default(Value).Item;", "initialize,before"),
        ("static-abstract", "interface IValue<T> where T : IValue<T> { static abstract int Item { get; } } class Value : IValue<Value> { public static int Item => Sink.Before(); } static class Adapter { public static int Read<T>() where T : IValue<T> => T.Item; }", "Value", "_ = Adapter.Read<Value>();", "before"),
        ("conditional-get", "class Value { public int Item => Sink.Before(); }", "Value", "_ = Receiver()?.Item;", "receiver,before"),
        ("conditional-set", "class Value { public int Item { set => Sink.Before(); } }", "Value", "Receiver()?.Item = Sink.Argument();", "receiver,argument,before"),
        ("object-initializer", "class Value { public int Item { set => Sink.Before(); } }", "Value", "_ = new Value { Item = Sink.Argument() };", "argument,before"),
        ("index-initializer", "class Value { public int this[int index] { set => Sink.Before(); } }", "Value", "_ = new Value { [Sink.Index()] = Sink.Argument() };", "index,argument,before"),
        ("deconstruction", "class Value { public int Item { get => Sink.Unrelated(); set => Sink.Before(); } }", "Value", "(Receiver().Item, Receiver().Item) = (Sink.Argument(), Sink.Argument());", "receiver,receiver,argument,argument,before,before"),
        ("nested-initializer", "class Container { public Value Child { get { Sink.Receiver(); return new Value(); } } } class Value { public int Item { set => Sink.Before(); } }", "Value", "_ = new Container { Child = { Item = Sink.Argument() } };", "receiver,argument,before"),
        ("property-pattern", "class Value { public int Item => Sink.Before(); }", "Value", "_ = Receiver() is { Item: > 0 };", "receiver,before"),
        ("init", "class Value { public int Item { init => Sink.Before(); } }", "Value", "_ = new Value { Item = Sink.Argument() };", "argument,before"),
        ("interface-set", "interface IValue { int Item { set; } } class Value : IValue { public int Item { set => Sink.Before(); } }", "IValue", "Receiver().Item = Sink.Argument();", "receiver,argument,before"),
        ("generic", "class Generic<T> { public static int Item => Sink.Before(); } class Value {}", "Value", "_ = Generic<int>.Item; _ = Generic<string>.Item;", "before,before"),
        ("suppressed-read", "class Value { public int Item => Sink.Before(); }", "Value", "_ = Receiver().Item!;", "receiver,before"),
        ("nested-initializer-multiple", "class Container { public Value Child { get { Sink.Getter(); return new Value(); } } } class Value { public int Item { set => Sink.Before(); } public int Other { set => Sink.Before(); } }", "Value", "_ = new Container { Child = { Item = Sink.Argument(), Other = Sink.Argument() } };", "getter,argument,before,getter,argument,before"),
        ("nested-index-initializer", "class Container { public Value this[int index] { get { Sink.Getter(); return new Value(); } } } class Value { public int Item { set => Sink.Before(); } public int Other { set => Sink.Before(); } }", "Value", "_ = new Container { [Sink.Index()] = { Item = Sink.Argument(), Other = Sink.Argument() } };", "index,getter,argument,before,getter,argument,before"),
        ("interface-pattern", "interface IValue { int Item { get; } } class Value : IValue { public int Item => Sink.Before(); }", "IValue", "_ = Receiver() is { Item: > 0 };", "receiver,before"),
        ("virtual-initializer", "class Base { public virtual int Item { set {} } } class Value : Base { public override int Item { set => Sink.Before(); } } class Other : Value { public override int Item { set => Sink.Unrelated(); } }", "Value", "_ = new Value { Item = Sink.Argument() };", "argument,before"),
        ("auto-interface", "interface IValue { int Item { get; } } class Value : IValue { public int Item { get; } = Sink.Before(); }", "IValue", "_ = Receiver().Item;", "receiver,before"),
        ("nested-chain", "class Container { public Middle Child { get { Sink.Receiver(); return new Middle(); } } } class Middle { public Value Child { get { Sink.Getter(); return new Value(); } } } class Value { public int Item { set => Sink.Before(); } public int Other { set => Sink.Before(); } }", "Value", "_ = new Container { Child = { Child = { Item = Sink.Argument(), Other = Sink.Argument() } } };", "receiver,getter,argument,before,receiver,getter,argument,before"),
        ("nested-index-chain", "class Container { public Middle Child { get { Sink.Receiver(); return new Middle(); } } } class Middle { public Value this[int index] { get { Sink.Getter(); return new Value(); } } } class Value { public int Item { set => Sink.Before(); } public int Other { set => Sink.Before(); } }", "Value", "_ = new Container { Child = { [Sink.Index()] = { Item = Sink.Argument(), Other = Sink.Argument() } } };", "index,receiver,getter,argument,before,receiver,getter,argument,before"),
        ("nested-collection", "class Container { public Value Child { get { Sink.Getter(); return new Value(); } } } class Value : System.Collections.IEnumerable { public void Add(int value) => Sink.Before(); public System.Collections.IEnumerator GetEnumerator() => throw new System.NotSupportedException(); }", "Value", "_ = new Container { Child = { Sink.Argument(), Sink.Argument() } };", "getter,argument,before,getter,argument,before"),
        ("record-property", "record Value(int Item) { public Value() : this(Sink.Before()) {} }", "Value", "_ = Receiver().Item;", "receiver,before"),
        ("record-interface", "interface IValue { int Item { get; } } record Value(int Item) : IValue { public Value() : this(Sink.Before()) {} }", "IValue", "_ = Receiver().Item;", "receiver,before"),
        ("field-keyword", "class Value { static Value() {} public static int Item { get { Sink.Before(); return field; } } = Sink.Initialize(); }", "Value", "_ = Value.Item;", "initialize,before"),
        ("partial-property", "partial class Value { public partial int Item { get; } } partial class Value { public partial int Item => Sink.Before(); }", "Value", "_ = Receiver().Item;", "receiver,before"),
        ("switch-expression", "class Value { public int Item => Sink.Before(); }", "Value", "_ = Receiver() switch { { Item: > 0 } => 1, _ => 0 };", "receiver,before"),
        ("switch-statement", "class Value { public int Item => Sink.Before(); }", "Value", "switch (Receiver()) { case { Item: > 0 }: break; }", "receiver,before"),
        ("sealed-pattern", "interface IValue { int Item { get; } } class Value : IValue { public int Item => Sink.Before(); }", "IValue", "_ = Receiver() is { Item: > 0 };", "receiver,before"),
        ("parenthesized-set", "class Base { public virtual int Item { set {} } } class Value : Base { public override int Item { set => Sink.Before(); } }", "Base", "((Receiver().Item)) = Sink.Argument();", "receiver,argument,before"),
        ("overloaded-indexer", "class Value { public int this[int index] => Sink.Before(); public int this[string index] => Sink.Unrelated(); }", "Value", "_ = Receiver()[Sink.Index()];", "receiver,index,before"),
        ("compound-conversion", "struct Amount { public static int operator +(Amount left, int right) => Sink.Operation(); public static implicit operator Amount(int value) { Sink.Conversion(); return default; } } class Value { public Amount Item { get { Sink.Getter(); return default; } set => Sink.Before(); } }", "Value", "Receiver().Item += Sink.Argument();", "receiver,getter,argument,operation,conversion,before")
    ];

    public static IEnumerable<object[]> Cases => Examples.Select(example => new object[] { example.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task AccessorBodiesAndEvaluationOrder(string name) => VerifyAccessor(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceAccessorBodiesAndEvaluationOrder(string name) => VerifyAccessor(name, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliFindsAccessorChanges(bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario(Examples.Single(example => example.Name == "interface")));
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
            if (command == "diff") arguments.Add(fixture.After);
            arguments.AddRange(["--entry", "Entry.Run", "--format", "json"]);
            if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
            if (workspace) arguments.AddRange(["--project", "App.csproj"]);
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            Assert.Contains("Sink.After", output);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Integration")]
    public async Task BeforeFieldInitRequiresStorageAccess(bool workspace, bool automatic)
    {
        var source = "static class Value { static int storage = Initialize(); static int Initialize() { Sink.Before(); return 1; } public static int Item "
            + (automatic ? "{ get; } = Initialize();" : "=> 1;")
            + " } static class Entry { public static void Run() { _ = Value.Item; } } static class Sink { public static void Before() {} public static void After() {} }";
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("property-storage", "Custom static accessors do not imply backing-field access.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        using var query = JsonDocument.Parse(await fixture.QueryAsync(options));
        Assert.Equal(automatic, Walk(query.RootElement.GetProperty("trees")).Any(node => node.GetProperty("label").GetString() == "Sink.After"));
        using var diff = JsonDocument.Parse(await fixture.DiffAsync(options));
        Assert.Equal(automatic, diff.RootElement.GetProperty("hasChanges").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task IndexersRetainTheirDeclaringProject(bool workspace)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><Compile Remove=\"Lib/**/*.cs\"/><ProjectReference Include=\"Lib/Lib.csproj\"/></ItemGroup></Project>",
            ["Lib/Lib.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "static class Entry { public static void Run() { IValue value = new Value(); _ = value[1]; } }",
            ["Lib/Value.cs"] = "public interface IValue { int this[int index] { get; } } public class Value : IValue { public int this[int index] => Sink.Before(); } public static class Sink { public static int Before() => 1; public static int After() => 2; }"
        };
        var after = new Dictionary<string, string>(before) { ["Lib/Value.cs"] = before["Lib/Value.cs"].Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("property-project", "Indexer dispatch retains project and parameter identities.", before, after, []), workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Sink.After" : null);
            Assert.Contains("Sink.After", output);
            using var json = JsonDocument.Parse(output);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var accessor = Assert.Single(Walk(json.RootElement.GetProperty("trees")), node => node.GetProperty("label").GetString() == "IValue.get_Item → Value.get_Item");
            var side = accessor.GetProperty("after");
            Assert.Equal("Lib/Value.cs", side.GetProperty("definition").GetProperty("path").GetString());
            Assert.EndsWith("Value.get_Item(int)", side.GetProperty("symbolId").GetString());
            if (workspace) Assert.StartsWith("project:Lib/Lib.csproj@net11.0::", side.GetProperty("symbolId").GetString());
        }
    }

    private static async Task VerifyAccessor(string name, bool workspace)
    {
        var example = Examples.Single(example => example.Name == name);
        var scenario = CreateScenario(example);
        Assert.Equal(example.Runtime, Execute(scenario.Before["Flow.cs"]));
        var expected = example.Runtime.Replace("before", "after", StringComparison.Ordinal);
        Assert.Equal(expected, Execute(scenario.After["Flow.cs"]));
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 25, Context = -1 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Sink.After" : null);
            foreach (var output in outputs.Values)
            {
                Assert.Contains("Sink.After", output);
                Assert.DoesNotContain("Sink.Unrelated", output);
            }
            using var json = JsonDocument.Parse(outputs["json"]);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var nodes = Walk(json.RootElement.GetProperty("trees")).ToArray();
            var trace = nodes.Select(node => node.GetProperty("label").GetString()!)
                .Where(label => label.StartsWith("Sink.", StringComparison.Ordinal)).Select(label => label[5..].ToLowerInvariant());
            Assert.Equal(expected, string.Join(',', trace));
            Assert.DoesNotContain(nodes, node => node.GetProperty("omission") is { ValueKind: JsonValueKind.Object } omission
                && omission.GetProperty("reason").GetString() == "cycle");
            if (name.StartsWith("record-", StringComparison.Ordinal)) Assert.Contains(nodes, node => node.GetProperty("label").GetString()!.Contains("Value.get_Item", StringComparison.Ordinal));
            var accessor = nodes.First(node => node.GetProperty("label").GetString()!.Contains("get_", StringComparison.Ordinal)
                || node.GetProperty("label").GetString()!.Contains("set_", StringComparison.Ordinal));
            Assert.Equal("Flow.cs", accessor.GetProperty("after").GetProperty("definition").GetProperty("path").GetString());
        }
        using var automatic = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { MaxDepth = 25 }));
        Assert.Contains(automatic.RootElement.GetProperty("trees").EnumerateArray(), node => node.GetProperty("label").GetString() == "Entry.Run");
    }

    private static Scenario CreateScenario((string Name, string Declaration, string ReceiverType, string Invoke, string Runtime) example)
    {
        var source = example.Declaration + " public static class Entry { public static string Run() { " + example.Invoke
            + " return string.Join(\",\", Sink.Trace); } static " + example.ReceiverType + " Receiver() { Sink.Receiver(); return new Value(); } } "
            + "public static class Sink { public static readonly System.Collections.Generic.List<string> Trace = new(); "
            + string.Join(' ', new[] { "Before", "After", "Receiver", "Argument", "Getter", "Index", "Initialize", "Unrelated", "Operation", "Conversion" }
                .Select(name => "public static int " + name + "() { Trace.Add(\"" + name.ToLowerInvariant() + "\"); return 1; }")) + " }";
        if (example.Name == "sealed-pattern") source = source.Replace("public static class Entry", "public sealed class Entry", StringComparison.Ordinal);
        var before = new Dictionary<string, string>
        {
            ["Flow.cs"] = source,
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal) };
        return new Scenario("property-accessor-" + example.Name, "Property access preserves receiver, index, value and accessor evaluation order.", before, after, []);
    }

    private static string Execute(string source)
    {
        var compilation = new SourceOnlyAnalysisProvider().CreateCompilation([CSharpSyntaxTree.ParseText(source)])
            .WithAssemblyName("PropertyAudit" + Guid.NewGuid().ToString("N"));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static IEnumerable<JsonElement> Walk(JsonElement nodes) =>
        nodes.EnumerateArray().SelectMany(node => new[] { node }.Concat(Walk(node.GetProperty("children"))));
}
