using System.Runtime.Loader;
using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class OperatorInitializationTests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        "binary", "unary", "implicit", "explicit", "checked-conversion", "truth", "logical", "repeated", "generic", "static-abstract", "static-abstract-interface", "abstract-conversion", "abstract-method", "instance-struct", "self-reference"
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task OperatorsReachTypeInitialization(string name) => VerifyInitialization(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceOperatorsReachTypeInitialization(string name) => VerifyInitialization(name, true);

    private static async Task VerifyInitialization(string name, bool workspace)
    {
        var (scenario, expected) = CreateScenario(name);
        Assert.Equal(expected, Execute(scenario.Before["Flow.cs"]));
        Assert.Equal(expected.Replace("before", "after", StringComparison.Ordinal), Execute(scenario.After["Flow.cs"]));
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20, Context = -1 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Sink.After" : null);
            foreach (var output in outputs.Values)
            {
                Assert.Contains("Entry.Run", output);
                Assert.Contains("Sink.After", output);
                Assert.Contains("initialization of Value", output);
                Assert.DoesNotContain("Sink.Unrelated", output);
            }
            using var json = JsonDocument.Parse(outputs["json"]);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var nodes = Walk(json.RootElement.GetProperty("trees")).ToArray();
            Assert.Equal(name == "generic" ? 2 : 1, nodes.Count(node => node.GetProperty("label").GetString() == "Sink.After"));
            Assert.DoesNotContain(nodes, node => node.GetProperty("detail").GetString() == "cycle");
            var labels = nodes.Select(node => node.GetProperty("label").GetString()).ToArray();
            Assert.True(Array.IndexOf(labels, "Sink.Left") < Array.IndexOf(labels, "Sink.After"));
            Assert.True(Array.IndexOf(labels, "Sink.After") < Array.IndexOf(labels, "Sink.Body"));
            if (name is "binary" or "repeated" or "generic" or "static-abstract" or "static-abstract-interface" or "instance-struct")
                Assert.True(Array.IndexOf(labels, "Sink.Right") < Array.IndexOf(labels, "Sink.After"));
            if (name == "logical") Assert.True(Array.IndexOf(labels, "Sink.After") < Array.IndexOf(labels, "Sink.Right"));
        }
        using var automatic = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { MaxDepth = 20 }));
        Assert.Contains(automatic.RootElement.GetProperty("trees").EnumerateArray(), node => node.GetProperty("label").GetString() == "Entry.Run");
    }

    [Theory]
    [InlineData("binary", false)]
    [InlineData("binary", true)]
    [InlineData("implicit", false)]
    [InlineData("implicit", true)]
    [Trait("Layer", "Integration")]
    public async Task CliFindsInitializerChangesThroughOperators(string name, bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario(name).Scenario);
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
            if (command == "diff") arguments.Add(fixture.After);
            arguments.AddRange(["--entry", "Entry.Run", "--format", "json"]);
            if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
            if (workspace) arguments.AddRange(["--project", "App.csproj"]);
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            Assert.Contains("initialization of Value", output);
            Assert.Contains("Sink.After", output);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Integration")]
    public async Task OperatorInitializationRetainsItsDeclaringProject(bool conversion, bool workspace)
    {
        var operation = conversion ? "public static implicit operator int(Value value) => 1;"
            : "public static Value operator +(Value left, Value right) => left;";
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><Compile Remove=\"Lib/**/*.cs\"/><ProjectReference Include=\"Lib/Lib.csproj\"/></ItemGroup></Project>",
            ["Lib/Lib.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = "public static class Entry { public static void Run() { " + (conversion ? "int value = default(Value);" : "_ = default(Value) + default(Value);") + " } }",
            ["Lib/Value.cs"] = "public class Value { static Value() { Sink.Before(); } " + operation + " } public static class Sink { public static void Before() {} public static void After() {} }"
        };
        var after = new Dictionary<string, string>(before) { ["Lib/Value.cs"] = before["Lib/Value.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        var scenario = new Scenario("operator-project-initialization", "Private type initialization follows the operator's declaring project.", before, after, []);
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options)
                : await fixture.QueryAsync(options, target: command == "reach" ? "Sink.After" : null);
            Assert.Contains("Sink.After", output);
            using var json = JsonDocument.Parse(output);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var initializer = Assert.Single(Walk(json.RootElement.GetProperty("trees")), node => node.GetProperty("label").GetString() == "initialization of Value");
            var member = initializer.GetProperty("after");
            Assert.Equal("Lib/Value.cs", member.GetProperty("definition").GetProperty("path").GetString());
            if (workspace) Assert.StartsWith("project:Lib/Lib.csproj@net11.0::", member.GetProperty("symbolId").GetString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Integration")]
    public async Task BeforeFieldInitOperatorsDoNotInventFieldReads(bool conversion, bool workspace)
    {
        var scenario = CreateScenario(conversion ? "implicit" : "binary").Scenario;
        var before = new Dictionary<string, string>(scenario.Before)
        {
            ["Flow.cs"] = scenario.Before["Flow.cs"].Replace("static Value() { Sink.Before(); }", "static readonly int value = Initialize(); static int Initialize() { Sink.Before(); return 1; }", StringComparison.Ordinal)
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = before["Flow.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        await using var fixture = await AnalysisFixture.CreateAsync(scenario with { Before = before, After = after }, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 20 };
        using var tree = JsonDocument.Parse(await fixture.QueryAsync(options));
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Contains(Walk(tree.RootElement.GetProperty("trees")), node => node.GetProperty("label").GetString()!.Contains(".op_", StringComparison.Ordinal));
        Assert.DoesNotContain(Walk(tree.RootElement.GetProperty("trees")), node => node.GetProperty("label").GetString() == "Sink.After");
        using var diff = JsonDocument.Parse(await fixture.DiffAsync(options));
        Assert.False(diff.RootElement.GetProperty("hasChanges").GetBoolean());
        using var reach = JsonDocument.Parse(await fixture.QueryAsync(options, target: "Sink.After"));
        Assert.Empty(reach.RootElement.GetProperty("paths").EnumerateArray());
    }

    private static string Execute(string source)
    {
        var compilation = new SourceOnlyAnalysisProvider().CreateCompilation([CSharpSyntaxTree.ParseText(source)])
            .WithAssemblyName("InitializerAudit" + Guid.NewGuid().ToString("N"));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
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

    private static (Scenario Scenario, string Runtime) CreateScenario(string name)
    {
        const string addition = "public static Value operator +(Value left, Value right) { Sink.Body(); return left; }";
        var (declaration, operations, invoke, runtime, extra) = name switch
        {
            "unary" => ("class Value", "public static Value operator -(Value value) { Sink.Body(); return value; }", "_ = Left(); _ = -Left();", "left,left,before,body", ""),
            "implicit" => ("class Value", "public static implicit operator int(Value value) { Sink.Body(); return 1; }", "int value = Left();", "left,before,body", ""),
            "explicit" => ("class Value", "public static explicit operator int(Value value) { Sink.Body(); return 1; }", "_ = (int)Left();", "left,before,body", ""),
            "checked-conversion" => ("class Value", "public static explicit operator int(Value value) => 0; public static explicit operator checked int(Value value) { Sink.Body(); return 1; }", "_ = checked((int)Left());", "left,before,body", ""),
            "truth" => ("class Value", "public static bool operator true(Value value) { Sink.Body(); return true; } public static bool operator false(Value value) => false;", "if (Left()) {}", "left,before,body", ""),
            "logical" => ("class Value", "public static bool operator true(Value value) => true; public static bool operator false(Value value) { Sink.Body(); return false; } public static Value operator &(Value left, Value right) { Sink.Body(); return left; }", "_ = Left() && Right();", "left,before,body,right,body", ""),
            "repeated" => ("class Value", addition, "_ = Left() + Right(); _ = Left() + Right();", "left,right,before,body,left,right,body", ""),
            "generic" => ("class Value<T>", "public static Value<T> operator +(Value<T> left, Value<T> right) { Sink.Body(); return left; }", "_ = Left<int>() + Right<int>(); _ = Left<string>() + Right<string>();", "left,right,before,body,left,right,before,body", ""),
            "static-abstract" => ("struct Value : IAdd<Value>", addition, "_ = Adapter.Add(Left(), Right());", "left,right,before,body", "interface IAdd<T> where T : IAdd<T> { static abstract T operator +(T left, T right); } static class Adapter { public static T Add<T>(T left, T right) where T : IAdd<T> => left + right; }"),
            "static-abstract-interface" => ("struct Value : IAdd<Value>", addition, "_ = Adapter.Add(Left(), Right());", "left,right,before,body", "interface IAdd<T> where T : IAdd<T> { static IAdd() { Sink.Unrelated(); } static abstract T operator +(T left, T right); } static class Adapter { public static T Add<T>(T left, T right) where T : IAdd<T> => left + right; }"),
            "abstract-conversion" => ("struct Value : IConvert<Value>", "public static explicit operator int(Value value) { Sink.Body(); return 1; }", "_ = Adapter.Convert(Left());", "left,before,body", "interface IConvert<T> where T : IConvert<T> { static IConvert() { Sink.Unrelated(); } static abstract explicit operator int(T value); } static class Adapter { public static int Convert<T>(T value) where T : IConvert<T> => (int)value; }"),
            "abstract-method" => ("struct Value : IRun<Value>", "public static void Call(Value value) { Sink.Body(); }", "Adapter.Call(Left());", "left,before,body", "interface IRun<T> where T : IRun<T> { static IRun() { Sink.Unrelated(); } static abstract void Call(T value); } static class Adapter { public static void Call<T>(T value) where T : IRun<T> => T.Call(value); }"),
            "instance-struct" => ("struct Value", "public void operator +=(Value value) { Sink.Body(); }", "var value = Left(); value += Right();", "left,right,before,body", ""),
            "self-reference" => ("class Value", addition, "_ = Left() + Right();", "left,right,before,body,body", ""),
            _ => ("class Value", addition, "_ = Left() + Right();", "left,right,before,body", "")
        };
        var operands = name == "generic"
            ? "static Value<T> Left<T>() { Sink.Left(); return default; } static Value<T> Right<T>() { Sink.Right(); return default; }"
            : "static Value Left() { Sink.Left(); return default; } static Value Right() { Sink.Right(); return default; }";
        var source = $$"""
            {{declaration}} { static Value() { Sink.Before(); } {{operations}} }
            {{extra}}
            public static class Entry { {{operands}} public static string Run() { {{invoke}} return Sink.Trace; } }
            static class Sink {
                public static string Trace = "";
                static void Add(string value) { Trace += (Trace.Length == 0 ? "" : ",") + value; }
                public static void Before() => Add("before");
                public static void After() => Add("after");
                public static void Left() => Add("left");
                public static void Right() => Add("right");
                public static void Body() => Add("body");
                public static void Unrelated() => Add("unrelated");
            }
            """;
        if (name == "self-reference") source = source.Replace("static Value() { Sink.Before(); }", "static Value() { Sink.Before(); _ = default(Value) + default(Value); }", StringComparison.Ordinal);
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = source
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) };
        return (new Scenario("operator-initialization-" + name, "Operators and conversions preserve declaring-type initialization after operand evaluation.", before, after, []), runtime);
    }

    private static IEnumerable<JsonElement> Walk(JsonElement nodes) =>
        nodes.EnumerateArray().SelectMany(node => new[] { node }.Concat(Walk(node.GetProperty("children"))));
}
