using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class ReceiverContextTests
{
    public static IEnumerable<object[]> Cases()
    {
        const string sinks = " static class Sink { public static void Left() {} public static void Right() {} public static void Child() {} public static void Other() {} }";
        var fixtures = new (string Name, string Source, string Entry, string[] Expected)[]
        {
            ("closed-parameter-helper", "abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base { protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); } class Router<T> where T : Base { public static void Run(T value) => value.Shared(); } static class Entry { public static void Run() => Router<Left>.Run(new Left()); }", "Entry.Run", ["Sink.Left"]),
            ("closed-parameter-interface", "interface IWorker { void Run(); } sealed class Left : IWorker { public void Run() => Sink.Left(); } sealed class Right : IWorker { public void Run() => Sink.Right(); } class Router<T> where T : IWorker { public static void Run(T value) => value.Run(); } static class Entry { public static void Run() => Router<Left>.Run(new Left()); }", "Entry.Run", ["Sink.Left"]),
            ("open-parameter-interface", "interface IWorker { void Run(); } sealed class Left : IWorker { public void Run() => Sink.Left(); } sealed class Right : IWorker { public void Run() => Sink.Right(); } class Router<T> where T : IWorker { public static void Run(T value) => value.Run(); }", "Router<T>.Run", ["Sink.Left", "Sink.Right"]),
            ("primary-base-constructor", "abstract class Base { protected Base(int value) => Hook(); protected abstract void Hook(); } sealed class Left(int value) : Base(value) { protected override void Hook() => Sink.Left(); } sealed class Right(int value) : Base(value) { protected override void Hook() => Sink.Right(); }", "new Left", ["Sink.Left"]),
            ("conditional-instance", "abstract class Base { protected void Shared() => ((Base)this)?.Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("exact-created-helper", "abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } class Left : Base { protected override void Hook() => Sink.Left(); } class Child : Left { protected override void Hook() => Sink.Child(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); } static class Entry { public static void Run() => new Left().Shared(); }", "Entry.Run", ["Sink.Left"]),
            ("exact-created-interface", "interface IWorker { void Run(); } abstract class Base : IWorker { public virtual void Run() => Hook(); protected abstract void Hook(); } class Left : Base { protected override void Hook() => Sink.Left(); } class Child : Left { protected override void Hook() => Sink.Child(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); } static class Entry { public static void Run() => ((IWorker)new Left()).Run(); }", "Entry.Run", ["Sink.Left"]),
            ("sealed-helper", "abstract class Base { protected void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("unsealed-helper", "abstract class Base { protected void Shared() => Hook(); protected abstract void Hook(); } class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } class Child : Left { protected override void Hook() => Sink.Child(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Child", "Sink.Left"]),
            ("base-call", "abstract class Base { public virtual void Run() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public override void Run() => base.Run(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Run", ["Sink.Left"]),
            ("method-group", "abstract class Base { protected System.Action Callback() => Shared; protected void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Callback(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("lambda", "abstract class Base { protected System.Action Callback() => () => Shared(); protected void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Callback(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("local-function", "abstract class Base { protected void Shared() { void Local() => Hook(); Local(); } protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("generic-base", "abstract class Base<T> { protected void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base<int> { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base<int> { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("inherited-interface", "interface IWorker { void Run(); } abstract class Base : IWorker { public virtual void Run() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => ((IWorker)this).Run(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("field-boundary", "abstract class Base { protected Base other = null!; protected void Shared() => other.Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left", "Sink.Right"]),
            ("parameter-boundary", "abstract class Base { protected void Shared(Base other) => other.Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry(Base other) => Shared(other); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left", "Sink.Right"]),
            ("static-boundary", "abstract class Base { protected void Shared() => Relay.Run(); public void Expose() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); } static class Relay { public static void Run() => new Right().Expose(); }", "Left.Entry", ["Sink.Right"]),
            ("base-constructor", "abstract class Base { protected Base() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public Left() : base() {} protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "new Left", ["Sink.Left"]),
            ("reference-cast", "abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => ((Base)this).Shared(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Left"]),
            ("conversion-boundary", "abstract class Base { public void Shared() => Hook(); protected abstract void Hook(); } sealed class Left : Base { public void Entry() => ((Right)this).Shared(); public static explicit operator Right(Left input) => new Right(); protected override void Hook() => Sink.Left(); } sealed class Right : Base { protected override void Hook() => Sink.Right(); }", "Left.Entry", ["Sink.Right"])
        };
        foreach (var fixture in fixtures)
            yield return [fixture.Name, fixture.Source + sinks, fixture.Entry, fixture.Expected];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Fast")]
    public Task InheritedCallsRetainReceiver(string name, string source, string entry, string[] expected) =>
        VerifyReceiver(name, source, entry, expected, false);

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Integration")]
    public Task WorkspaceInheritedCallsRetainReceiver(string name, string source, string entry, string[] expected) =>
        VerifyReceiver(name, source, entry, expected, true);

    private static async Task VerifyReceiver(string name, string source, string entry, string[] expected, bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var after = source.Replace("public static void Right() {}", "public static void Right() { throw new System.InvalidOperationException(); }", StringComparison.Ordinal);
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("receiver-context-" + name, "Inherited calls preserve the containing receiver and respect separate objects.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []), workspace);

        using var tree = Parse(await fixture.QueryAsync(new DiffOptions { Entries = [entry], MaxDepth = 16 }, before: true));
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        var actual = Flatten(tree.RootElement.GetProperty("trees")).Select(node => node.GetProperty("label").GetString()!)
            .Where(label => label.StartsWith("Sink.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        using var reach = Parse(await fixture.QueryAsync(new DiffOptions { Entries = [entry], MaxDepth = 16 }, before: true, target: "Sink.Right"));
        Assert.Empty(reach.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(expected.Contains("Sink.Right", StringComparer.Ordinal), reach.RootElement.GetProperty("paths").GetArrayLength() > 0);
        using var diff = Parse(await fixture.DiffAsync(new DiffOptions { Entries = [entry], MaxDepth = 16 }));
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(expected.Contains("Sink.Right", StringComparer.Ordinal), diff.RootElement.GetProperty("hasChanges").GetBoolean());
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task GenericReceiverCycleReferencesFirstInvocation() => VerifyCycle(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceGenericReceiverCycleReferencesFirstInvocation() => VerifyCycle(true);

    private static async Task VerifyCycle(bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        const string source = "class Box<T> { public void Run() => Run(); } class Entry<U> { public static void Start(Box<U> value) => value.Run(); }";
        var files = new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source };
        var after = new Dictionary<string, string>(files) { ["marker.txt"] = "after" };
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("receiver-context-cycle", "An unchanged generic receiver reuses its first active invocation.", files, after, []), workspace);

        using var tree = Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Entry<U>.Start"], MaxDepth = 16 }, before: true));
        Assert.Empty(tree.RootElement.GetProperty("diagnostics").EnumerateArray());
        var first = tree.RootElement.GetProperty("trees")[0].GetProperty("children")[0];
        var repeated = first.GetProperty("children")[0];
        Assert.Equal("cycle", repeated.GetProperty("omission").GetProperty("reason").GetString());
        Assert.Equal(first.GetProperty("id").GetString(), repeated.GetProperty("omission").GetProperty("referenceId").GetString());
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public Task SiblingOverridePreservesUnchangedSealedReceiver() => VerifySiblingOverride(false);

    [Fact]
    [Trait("Layer", "Integration")]
    public Task WorkspaceSiblingOverridePreservesUnchangedSealedReceiver() => VerifySiblingOverride(true);

    private static async Task VerifySiblingOverride(bool workspace)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        const string source = "class Base { protected void Shared() => Hook(); protected virtual void Hook() => Sink.Left(); } sealed class Left : Base { public void Entry() => Shared(); } sealed class Right : Base {} static class Sink { public static void Left() {} public static void Right() {} }";
        var after = source.Replace("sealed class Right : Base {}", "sealed class Right : Base { protected override void Hook() => Sink.Right(); }", StringComparison.Ordinal);
        await using var fixture = await AnalysisFixture.CreateAsync(new Scenario("receiver-context-sibling-override", "Adding a sibling override preserves the sealed receiver's unchanged call path.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = source },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []), workspace);

        using var unaffected = Parse(await fixture.DiffAsync(new DiffOptions { Entries = ["Left.Entry"], MaxDepth = 16 }));
        Assert.Empty(unaffected.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.False(unaffected.RootElement.GetProperty("hasChanges").GetBoolean());
        using var affected = Parse(await fixture.DiffAsync(new DiffOptions { Entries = ["Base.Shared"], MaxDepth = 16 }));
        Assert.Empty(affected.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.True(affected.RootElement.GetProperty("hasChanges").GetBoolean());
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            yield return node;
            foreach (var child in Flatten(node.GetProperty("children"))) yield return child;
        }
    }

    private static JsonDocument Parse(string output)
    {
        return JsonDocument.Parse(output);
    }
}
