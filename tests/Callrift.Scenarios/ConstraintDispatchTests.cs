using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ConstraintDispatchTests
{
    public static IEnumerable<object[]> Cases()
    {
        var examples = new (string Name, string Constraint, string Actual, string Declarations, bool Expected)[]
        {
            ("class-value", "class", "int", "", false),
            ("struct-reference", "struct", "string", "", false),
            ("struct-nullable", "struct", "int?", "", false),
            ("unmanaged-managed-field", "unmanaged", "Managed", "struct Managed { public string Value; }", false),
            ("constructor-missing", "new()", "NoDefault", "class NoDefault { public NoDefault(int value) {} }", false),
            ("constructor-abstract", "new()", "Abstract", "abstract class Abstract { public Abstract() {} }", false),
            ("base-unrelated", "Base", "Other", "class Base {} class Other {}", false),
            ("interface-unrelated", "IMarker", "Other", "interface IMarker {} class Other {}", false),
            ("unmanaged-generic-managed", "unmanaged", "Box<string>", "struct Box<T> { public T Value; }", false),
            ("constructor-private", "new()", "Private", "class Private { private Private() {} }", false),
            ("base-generic-unrelated", "Base<string>", "Derived", "class Base<T> {} class Derived : Base<int> {}", false),
            ("interface-value-unrelated", "IMarker", "int", "interface IMarker {}", false),
            ("class-array", "class", "int[]", "", true),
            ("struct-enum", "struct", "State", "enum State { One }", true),
            ("unmanaged-generic-value", "unmanaged", "Box<int>", "struct Box<T> { public T Value; }", true),
            ("constructor-value", "new()", "int", "", true),
            ("base-generic-derived", "Base<string>", "Derived", "class Base<T> {} class Derived : Base<string> {}", true),
            ("interface-value-implemented", "IMarker", "Marked", "interface IMarker {} struct Marked : IMarker {}", true),
            ("constructor-required", "new()", "Required", "class Required { public required int Value { get; init; } }", false),
            ("constructor-required-inherited", "new()", "Derived", "class Base { public required int Value { get; init; } } class Derived : Base {}", false),
            ("constructor-required-satisfied", "new()", "Required", "class Required { public required int Value { get; init; } [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public Required() { Value = 1; } }", true),
            ("constructor-enum", "new()", "State", "enum State { One }", true),
            ("constructor-nullable", "new()", "int?", "", true),
            ("ref-like-disallowed", "struct", "System.Span<int>", "", false),
            ("ref-like-allowed", "struct, allows ref struct", "System.Span<int>", "", true),
            ("unmanaged-metadata-token", "unmanaged", "System.Threading.CancellationToken", "", false),
            ("unmanaged-metadata-memory", "unmanaged", "System.ReadOnlyMemory<int>", "", false),
            ("unmanaged-metadata-date", "unmanaged", "System.DateTime", "", true),
            ("class-reference", "class", "string", "", true),
            ("struct-value", "struct", "int", "", true),
            ("unmanaged-value", "unmanaged", "int", "", true),
            ("constructor-public", "new()", "Default", "class Default { public Default() {} }", true),
            ("base-derived", "Base", "Derived", "class Base {} class Derived : Base {}", true),
            ("interface-implemented", "IMarker", "Marked", "interface IMarker {} class Marked : IMarker {}", true)
        };
        foreach (var example in examples)
            yield return [example.Name, example.Constraint, example.Actual, example.Declarations, example.Expected];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task ConstructedContractsRespectImplementationConstraints(string name, string constraint, string actual, string declarations, bool compatible) =>
        VerifyConstraints(name, false, constraint, actual, declarations, compatible);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceConstructedContractsRespectImplementationConstraints(string name, string constraint, string actual, string declarations, bool compatible) =>
        VerifyConstraints(name, true, constraint, actual, declarations, compatible);

    private static async Task VerifyConstraints(string name, bool workspace, string constraint, string actual, string declarations, bool compatible)
    {
        var source = $$"""
            {{declarations}}
            interface IHandler<T> { void Run(T value); }
            class Constrained<T> : IHandler<T> where T : {{constraint}} { public void Run(T value) => Sink.Before(); }
            class Unconstrained : IHandler<{{actual}}> { public void Run({{actual}} value) {} }
            class Flow { public void Run(IHandler<{{actual}}> handler) => handler.Run(default); }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        if (actual == "System.Span<int>") source = source.Replace("interface IHandler<T> {", "interface IHandler<T> where T : allows ref struct {", StringComparison.Ordinal);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("dispatch-constraint-" + name, "Only satisfiable generic implementations can follow a closed invariant contract.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal) }, []), workspace);
        using var diff = JsonDocument.Parse(await fixture.DiffAsync());
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(compatible ? "Flow.Run" : "Constrained<T>.Run", Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        using var tree = JsonDocument.Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] }));
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        var targets = tree.RootElement.GetProperty("trees")[0].GetProperty("children")[0].GetProperty("after").GetProperty("targetIds")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(compatible, targets.Any(target => target.Contains("Constrained<T>.Run", StringComparison.Ordinal)));
        Assert.Contains(targets, target => target.Contains("Unconstrained.Run", StringComparison.Ordinal));
        using var reach = JsonDocument.Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] }, target: "Sink.After"));
        Assert.Empty(reach.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(compatible ? 1 : 0, reach.RootElement.GetProperty("paths").GetArrayLength());
        Assert.False(reach.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task ConstraintOnlyEditsChangeCallerDispatch(bool remove) => VerifyConstraintEdits(false, remove);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceConstraintOnlyEditsChangeCallerDispatch(bool remove) => VerifyConstraintEdits(true, remove);

    private static async Task VerifyConstraintEdits(bool workspace, bool remove)
    {
        const string source = """
            interface IHandler<T> { void Run(T value); }
            class Constrained<T> : IHandler<T> where T : class { public void Run(T value) => Sink.Unchanged(); }
            class Unconstrained : IHandler<int> { public void Run(int value) {} }
            class Flow { public void Run(IHandler<int> handler) => handler.Run(1); }
            static class Sink { public static void Unchanged() {} }
            """;
        var valueConstraint = source.Replace("where T : class", "where T : struct", StringComparison.Ordinal);
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("constraint-only-dispatch", "Changing an implementation constraint changes the possible caller path without editing its body.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = remove ? valueConstraint : source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = remove ? source : valueConstraint }, []), workspace);
        using var diff = JsonDocument.Parse(await fixture.DiffAsync());
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Flow.Run", Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        foreach (var before in new[] { true, false })
        {
            using var reach = JsonDocument.Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] }, before: before, target: "Sink.Unchanged"));
            Assert.Empty(reach.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(before == remove ? 1 : 0, reach.RootElement.GetProperty("paths").GetArrayLength());
        }
    }

}
