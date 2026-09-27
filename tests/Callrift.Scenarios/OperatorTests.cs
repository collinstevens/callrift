using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class OperatorTests
{
    public static IEnumerable<object[]> Cases => new[] { "binary", "unary", "prefix", "postfix", "compound", "checked", "checked-unary", "checked-increment", "lifted", "generic", "condition", "and", "or", "compound-conversions", "instance-compound", "instance-increment", "nested-logical", "lambda-logical", "local-logical", "initializer-logical", "generic-logical", "inherited-logical", "condition-wrapped", "condition-when" }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task OperatorBodiesRemainReachable(string name) => VerifyOperators(name, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceOperatorBodiesRemainReachable(string name) => VerifyOperators(name, true);

    private static async Task VerifyOperators(string name, bool workspace)
    {
        var scenario = CreateScenario(name);
        foreach (var files in new[] { scenario.Before, scenario.After })
        {
            var compilation = new SourceOnlyAnalysisProvider().CreateCompilation([CSharpSyntaxTree.ParseText(files["Flow.cs"])]);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        }
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 10 };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var outputs = command == "diff" ? await fixture.DiffFormatsAsync(options)
                : await fixture.QueryFormatsAsync(options, target: command == "reach" ? "Sink.After" : null);
            foreach (var output in outputs.Values)
            {
                Assert.Contains("Entry.Run", output);
                Assert.Contains("Sink.After", output);
                Assert.DoesNotContain("Sink.Unrelated", output);
            }
            using var json = JsonDocument.Parse(outputs["json"]);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            if (command != "tree") continue;
            var children = json.RootElement.GetProperty("trees")[0].GetProperty("children").EnumerateArray().ToArray();
            var operators = Walk(children).Where(n => n.GetProperty("kind").GetString() == "call" && n.GetProperty("label").GetString()!.Contains(".op_", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(operators);
            foreach (var operation in operators) Assert.NotEmpty(operation.GetProperty("after").GetProperty("callSites").EnumerateArray());
            if (name.StartsWith("condition", StringComparison.Ordinal)) Assert.Single(operators);
            if (name == "nested-logical") Assert.Equal(["Value.op_False", "Value.op_BitwiseAnd", "Value.op_True", "Value.op_BitwiseOr"], Labels(operators));
            if (name is "lambda-logical" or "local-logical" or "initializer-logical") Assert.Equal(["Value.op_False", "Value.op_BitwiseAnd"], Labels(operators));
            if (name == "inherited-logical") Assert.Equal(["Base.op_False", "Base.op_BitwiseAnd"], Labels(operators));
            if (name == "generic-logical") Assert.Contains(operators, n => n.GetProperty("label").GetString()!.Contains("Number.op_False", StringComparison.Ordinal));
            if (name == "binary") Assert.Equal(["Entry.Left", "Entry.Right", "Value.op_Addition"], Labels(children));
            if (name == "unary") Assert.Equal(["Entry.Left", "Value.op_UnaryNegation"], Labels(children));
            if (name == "checked") Assert.Contains(operators, n => n.GetProperty("label").GetString() == "Value.op_CheckedAddition");
            if (name == "checked-unary") Assert.Contains(operators, n => n.GetProperty("label").GetString() == "Value.op_CheckedUnaryNegation");
            if (name == "checked-increment") Assert.Contains(operators, n => n.GetProperty("label").GetString() == "Value.op_CheckedIncrement");
            if (name == "compound-conversions") Assert.Equal(["new Input", "Input.op_Implicit", "Entry.Right", "Value.op_Addition", "Input.op_Implicit"], Labels(children));
            if (name is "and" or "or")
            {
                Assert.Equal("Entry.Left", children[0].GetProperty("label").GetString());
                Assert.Equal(name == "and" ? "Value.op_False" : "Value.op_True", children[1].GetProperty("label").GetString());
                Assert.Equal("branch", children[2].GetProperty("kind").GetString());
                Assert.Equal(["Entry.Right", name == "and" ? "Value.op_BitwiseAnd" : "Value.op_BitwiseOr"], Labels(children[2].GetProperty("children").EnumerateArray()));
                Assert.Equal(3, children.Length);
            }
            if (name == "condition") Assert.Equal(["Entry.Left", "Value.op_True", "if (Left())"], Labels(children));
        }
        using var automatic = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { MaxDepth = 10 }));
        Assert.Contains(automatic.RootElement.GetProperty("trees").EnumerateArray(), n => n.GetProperty("label").GetString() == "Entry.Run");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliFindsOperatorChanges(bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario("and"));
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var arguments = new List<string> { command, command == "diff" ? fixture.Before : fixture.After };
            if (command == "diff") arguments.Add(fixture.After);
            arguments.AddRange(["--entry", "Entry.Run", "--format", "json"]);
            if (command == "reach") arguments.AddRange(["--to", "Sink.After"]);
            if (workspace) arguments.AddRange(["--project", "App.csproj"]);
            var output = await fixture.RunAsync(arguments.ToArray());
            Assert.StartsWith("exit: 0\n", output);
            Assert.Contains("Entry.Run", output);
            Assert.Contains("Value.op_BitwiseAnd", output);
            Assert.Contains("Sink.After", output);
            Assert.DoesNotContain("unresolved-call", output);
        }
    }

    private static IEnumerable<string?> Labels(IEnumerable<JsonElement> nodes) => nodes.Select(n => n.GetProperty("label").GetString());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task TopLevelLogicalOperatorsRetainTheirTruthTest(bool workspace)
    {
        const string source = """
            _ = new Value() && new Value();
            struct Value {
                public static Value operator &(Value left, Value right) { Sink.Before(); return left; }
                public static bool operator true(Value value) => true;
                public static bool operator false(Value value) => false;
            }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>";
        var scenario = new Scenario("top-level-operators", "Short-circuit operators in application entry points retain the compiler-bound truth test.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Program.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Program.cs"] = source.Replace("Sink.Before", "Sink.After", StringComparison.Ordinal) }, []);
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Program.cs::<top-level>"] };
        foreach (var command in new[] { "diff", "tree", "reach" })
        {
            var output = command == "diff" ? await fixture.DiffAsync(options) : await fixture.QueryAsync(options, target: command == "reach" ? "Sink.After" : null);
            using var json = JsonDocument.Parse(output);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Contains("Value.op_BitwiseAnd", output);
            Assert.Contains("Sink.After", output);
            if (command == "tree") Assert.Contains("Value.op_False", output);
        }
    }

    private static IEnumerable<JsonElement> Walk(IEnumerable<JsonElement> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Walk(n.GetProperty("children").EnumerateArray())));

    private static Scenario CreateScenario(string name)
    {
        const string addition = "public static Value operator +(Value left, Value right) { Sink.Before(); return left; } public static Value operator +(Value left, int right) { Sink.Unrelated(); return left; }";
        const string logical = "public static Value operator &(Value left, Value right) { Sink.Before(); return left; } public static Value operator |(Value left, Value right) { Sink.Before(); return left; } public static bool operator true(Value value) { Sink.Test(); return true; } public static bool operator false(Value value) { Sink.Test(); return false; }";
        var (body, operators, extra) = name switch
        {
            "binary" => ("_ = Left() + Right();", addition, ""),
            "unary" => ("_ = -Left();", "public static Value operator -(Value value) { Sink.Before(); return value; }", ""),
            "prefix" => ("var value = Left(); _ = ++value;", "public static Value operator ++(Value value) { Sink.Before(); return value; }", ""),
            "postfix" => ("var value = Left(); _ = value++;", "public static Value operator ++(Value value) { Sink.Before(); return value; }", ""),
            "compound" => ("var value = Left(); value += Right();", addition, ""),
            "checked" => ("_ = checked(Left() + Right());", "public static Value operator +(Value left, Value right) { Sink.Unrelated(); return left; } public static Value operator checked +(Value left, Value right) { Sink.Before(); return left; }", ""),
            "checked-unary" => ("_ = checked(-Left());", "public static Value operator -(Value value) { Sink.Unrelated(); return value; } public static Value operator checked -(Value value) { Sink.Before(); return value; }", ""),
            "checked-increment" => ("var value = Left(); checked { value++; }", "public static Value operator ++(Value value) { Sink.Unrelated(); return value; } public static Value operator checked ++(Value value) { Sink.Before(); return value; }", ""),
            "lifted" => ("Value? left = Left(); Value? right = Right(); _ = left + right;", addition, ""),
            "generic" => ("_ = Adapter.Add(new Number(), new Number());", "", "interface IAdd<T> where T : IAdd<T> { static abstract T operator +(T left, T right); } struct Number : IAdd<Number> { public static Number operator +(Number left, Number right) { Sink.Before(); return left; } } struct Other : IAdd<Other> { public static Other operator +(Other left, Other right) { Sink.Unrelated(); return left; } } static class Adapter { public static T Add<T>(T left, T right) where T : IAdd<T> => left + right; }"),
            "condition" => ("if (Left()) Sink.Body();", "public static bool operator true(Value value) { Sink.Before(); return true; } public static bool operator false(Value value) { Sink.Unrelated(); return false; }", ""),
            "and" => ("_ = Left() && Right();", logical, ""),
            "or" => ("_ = Left() || Right();", logical, ""),
            "compound-conversions" => ("Input input = new(); input += Right();", "public static Result operator +(Operand left, Value right) { Sink.Before(); return new(); }", "struct Input { public static implicit operator Operand(Input value) => new(); public static implicit operator Input(Result value) => new(); } struct Operand {} struct Result {}"),
            "instance-compound" => ("var value = Left(); value += 1;", "public void operator +=(int value) { Sink.Before(); }", ""),
            "instance-increment" => ("var value = Left(); value++;", "public void operator ++() { Sink.Before(); }", ""),
            "nested-logical" => ("_ = (Left() && Right()) || Right();", logical, ""),
            "lambda-logical" => ("System.Func<Value> callback = () => Left() && Right(); _ = callback();", logical, ""),
            "local-logical" => ("Value Local() => Left() && Right(); _ = Local();", logical, ""),
            "initializer-logical" => ("_ = new Holder();", logical, "class Holder { private Value value = Entry.Left() && Entry.Right(); }"),
            "generic-logical" => ("_ = Adapter.And(new Number(), new Number());", "", "interface ILogic<T> where T : ILogic<T> { static abstract T operator &(T left, T right); static abstract bool operator true(T value); static abstract bool operator false(T value); } struct Number : ILogic<Number> { public static Number operator &(Number left, Number right) { Sink.Before(); return left; } public static bool operator true(Number value) => true; public static bool operator false(Number value) => false; } struct Other : ILogic<Other> { public static Other operator &(Other left, Other right) { Sink.Unrelated(); return left; } public static bool operator true(Other value) { Sink.Unrelated(); return true; } public static bool operator false(Other value) { Sink.Unrelated(); return false; } } static class Adapter { public static T And<T>(T left, T right) where T : ILogic<T> => left && right; }"),
            "inherited-logical" => ("_ = new Derived() && new Derived();", "", "class Base { public static Base operator &(Base left, Base right) { Sink.Before(); return left; } public static bool operator true(Base value) => true; public static bool operator false(Base value) => false; } class Derived : Base { public static bool operator true(Derived value) { Sink.Unrelated(); return true; } public static bool operator false(Derived value) { Sink.Unrelated(); return false; } }"),
            "condition-wrapped" => ("if (checked((Left()))!) Sink.Body();", "public static bool operator true(Value value) { Sink.Before(); return true; } public static bool operator false(Value value) { Sink.Unrelated(); return false; }", ""),
            "condition-when" => ("switch (0) { case 0 when Left(): Sink.Body(); break; }", "public static bool operator true(Value value) { Sink.Before(); return true; } public static bool operator false(Value value) { Sink.Unrelated(); return false; }", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var source = $$"""
            static class Entry {
                public static void Run() { {{body}} }
                public static Value Left() => new();
                public static Value Right() => new();
            }
            struct Value { {{operators}} }
            {{extra}}
            static class Sink { public static void Before() {} public static void After() {} public static void Unrelated() {} public static void Test() {} public static void Body() {} }
            """;
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        return new Scenario("operator-" + name, "User-defined operators preserve binding, caller reachability and operand evaluation order.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before", "Sink.After", StringComparison.Ordinal) }, []);
    }
}
