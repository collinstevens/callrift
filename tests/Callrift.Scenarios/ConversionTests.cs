using System.Text.Json;
using Callrift.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConversionTests
{
    [Theory]
    [InlineData("implicit")]
    [InlineData("explicit")]
    [InlineData("checked")]
    [InlineData("lifted")]
    [InlineData("conditional")]
    [InlineData("generic")]
    [InlineData("wrappers")]
    [InlineData("explicit-callback")]
    [InlineData("implicit-callback")]
    [InlineData("static-abstract")]
    [Trait("Layer", "Fast")]
    public Task ConversionBodiesRemainReachable(string name) => VerifyConversion(name, false);

    [Theory]
    [InlineData("implicit")]
    [InlineData("explicit")]
    [InlineData("checked")]
    [InlineData("lifted")]
    [InlineData("conditional")]
    [InlineData("generic")]
    [InlineData("wrappers")]
    [InlineData("explicit-callback")]
    [InlineData("implicit-callback")]
    [InlineData("static-abstract")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceConversionBodiesRemainReachable(string name) => VerifyConversion(name, true);

    private static async Task VerifyConversion(string name, bool workspace)
    {
        var scenario = CreateScenario(name);
        foreach (var files in new[] { scenario.Before, scenario.After })
        {
            var compilation = new SourceOnlyAnalysisProvider().CreateCompilation([CSharpSyntaxTree.ParseText(files["Flow.cs"])]);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        }
        await using var fixture = await AnalysisFixture.CreateAsync(scenario, workspace);
        var options = new DiffOptions { Entries = ["Entry.Run"], MaxDepth = 8 };
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
            var conversion = Assert.Single(Walk(children), n => n.GetProperty("kind").GetString() == "call" && n.GetProperty("label").GetString()!.Contains(".op_", StringComparison.Ordinal));
            var expected = name == "checked" ? "op_CheckedExplicit" : name.StartsWith("explicit", StringComparison.Ordinal) || name == "static-abstract" ? "op_Explicit" : "op_Implicit";
            Assert.EndsWith(expected, conversion.GetProperty("label").GetString());
            Assert.NotEmpty(conversion.GetProperty("after").GetProperty("callSites").EnumerateArray());
            if (name is "implicit" or "explicit" or "checked")
                Assert.Equal(["Entry.Get", "Value." + expected, "Entry.Tail", "Entry.Use"], children.Select(n => n.GetProperty("label").GetString()));
            if (name.EndsWith("-callback", StringComparison.Ordinal)) Assert.Contains("Entry.Callback", outputs["json"]);
        }
        using var automatic = JsonDocument.Parse(await fixture.DiffAsync(new DiffOptions { MaxDepth = 8 }));
        Assert.Contains(automatic.RootElement.GetProperty("trees").EnumerateArray(), n => n.GetProperty("label").GetString() == "Entry.Run");
    }

    private static IEnumerable<JsonElement> Walk(IEnumerable<JsonElement> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Walk(n.GetProperty("children").EnumerateArray())));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task CliFindsConversionChanges(bool workspace)
    {
        await using var fixture = await GitFixture.CreateAsync(CreateScenario("implicit"));
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
            Assert.Contains("Value.op_Implicit", output);
            Assert.Contains("Sink.After", output);
            Assert.DoesNotContain("unresolved-call", output);
        }
    }

    private static Scenario CreateScenario(string name)
    {
        var (body, conversions, extra) = name switch
        {
            "implicit" => ("Use(((Get())), Tail());", "public static implicit operator int(Value value) { Sink.Before(); return 1; } public static implicit operator string(Value value) { Sink.Unrelated(); return \"\"; }", ""),
            "explicit" => ("Use((int)Get(), Tail());", "public static explicit operator int(Value value) { Sink.Before(); return 1; }", ""),
            "checked" => ("Use(checked((int)Get()), Tail());", "public static explicit operator int(Value value) { Sink.Unrelated(); return 1; } public static explicit operator checked int(Value value) { Sink.Before(); return 1; }", ""),
            "lifted" => ("Value? value = Get(); int? result = value;", "public static implicit operator int(Value value) { Sink.Before(); return 1; }", ""),
            "conditional" => ("int value = (Flag() ? Get() : Get());", "public static implicit operator int(Value value) { Sink.Before(); return 1; }", ""),
            "generic" => ("Work work = new Box<Work>();", "", "class Box<T> where T : IWork, new() { public static implicit operator T(Box<T> value) { new T().Run(); return new T(); } } interface IWork { void Run(); } class Work : IWork { public void Run() => Sink.Before(); } class OtherWork : IWork { public void Run() => Sink.Unrelated(); }"),
            "wrappers" => ("int value = checked(unchecked(((Get())!)));", "public static implicit operator int(Value value) { Sink.Before(); return 1; }", ""),
            "explicit-callback" => ("Accept((Value)(System.Action)Callback);", "public static explicit operator Value(System.Action action) { Sink.Before(); return new(); }", ""),
            "implicit-callback" => ("Accept((System.Action)Callback);", "public static implicit operator Value(System.Action action) { Sink.Before(); return new(); }", ""),
            "static-abstract" => ("Adapter.Convert(new Convertible());", "", "interface IConvert<T> where T : IConvert<T> { static abstract explicit operator int(T value); } struct Convertible : IConvert<Convertible> { public static explicit operator int(Convertible value) { Sink.Before(); return 1; } } struct Other : IConvert<Other> { public static explicit operator int(Other value) { Sink.Unrelated(); return 1; } } static class Adapter { public static int Convert<T>(T value) where T : IConvert<T> => (int)value; }"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var source = $$"""
            static class Entry {
                public static void Run() { {{body}} }
                static Value Get() => new();
                static int Tail() => 0;
                static bool Flag() => true;
                static void Use(int value, int tail) {}
                static void Accept(Value value) {}
                static void Callback() {}
            }
            struct Value { {{conversions}} }
            {{extra}}
            static class Sink { public static void Before() {} public static void After() {} public static void Unrelated() {} }
            """;
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        return new Scenario("conversion-" + name, "User-defined conversions preserve identity, caller reachability and operand evaluation order.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before", "Sink.After", StringComparison.Ordinal) }, []);
    }
}
