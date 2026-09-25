using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class GenericDispatchTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvariantContractsExcludeIncompatibleImplementations(bool workspace, bool changeIncompatible)
    {
        const string source = """
            interface IConsumer<T> { void Consume(T value); }
            class Pair<T> {}
            class TextConsumer : IConsumer<string> { public void Consume(string value) {} }
            class NumberConsumer : IConsumer<int> { public void Consume(int value) {} }
            class OpenConsumer<T> : IConsumer<T> { public void Consume(T value) { Before(); } void Before() {} void After() {} }
            class WrappedConsumer<T> : IConsumer<Pair<T>> { public void Consume(Pair<T> value) { WrongBefore(); } void WrongBefore() {} void WrongAfter() {} }
            class Flow { public void Run(IConsumer<string> consumer) => consumer.Consume("value"); }
            """;
        var after = changeIncompatible ? source.Replace("WrongBefore();", "WrongAfter();", StringComparison.Ordinal)
            : source.Replace("{ Before(); }", "{ After(); }", StringComparison.Ordinal);
        await using var fixture = await CreateAsync(source, after);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var diff = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(diff);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal(changeIncompatible ? "WrappedConsumer<T>.Consume" : "Flow.Run", root.GetProperty("label").GetString());
        var tree = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", .. mode, "--format", "json"]);
        using var treeDocument = Parse(tree);
        var dispatch = treeDocument.RootElement.GetProperty("trees")[0].GetProperty("children")[0];
        Assert.Equal(["OpenConsumer<T>.Consume(T)", "TextConsumer.Consume(string)"],
            dispatch.GetProperty("after").GetProperty("targetIds").EnumerateArray().Select(v => v.GetString()!.Split("::", StringSplitOptions.None)[1]).ToArray());
        Assert.DoesNotContain("WrappedConsumer", tree);
        Assert.DoesNotContain("NumberConsumer", tree);
        var reach = await fixture.RunAsync(["reach", fixture.After, "--entry", "Flow.Run", "--to", "WrongAfter", .. mode, "--format", "json"]);
        using var reachDocument = Parse(reach);
        Assert.Empty(reachDocument.RootElement.GetProperty("paths").EnumerateArray());
        Assert.False(reachDocument.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedTypeParametersMustHaveConsistentArguments(bool workspace)
    {
        const string source = """
            using System.Collections.Generic;
            interface IConsumer<T> { void Consume(T value); }
            class Same<T> : IConsumer<KeyValuePair<T, T>> { public void Consume(KeyValuePair<T, T> value) { Wrong(); } void Wrong() {} }
            class Different<T, U> : IConsumer<KeyValuePair<T, U>> { public void Consume(KeyValuePair<T, U> value) { Before(); } void Before() {} void After() {} }
            class Flow { public void Run(IConsumer<KeyValuePair<string, int>> consumer) => consumer.Consume(default); }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
        var dispatch = root.GetProperty("children")[0];
        Assert.Single(dispatch.GetProperty("after").GetProperty("targetIds").EnumerateArray());
        Assert.Contains("Different<T, U>.After", output);
        Assert.DoesNotContain("Same<T>", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VariantContractsKeepCompatibleImplementations(bool workspace)
    {
        const string source = """
            interface IConsumer<in T> { void Consume(T value); }
            interface IProducer<out T> { T Produce(); }
            class ObjectConsumer : IConsumer<object> { public void Consume(object value) { Before(); } void Before() {} void After() {} }
            class TextProducer : IProducer<string> { public string Produce() { Before(); return "value"; } void Before() {} void After() {} }
            class Flow { public void Run(IConsumer<string> consumer, IProducer<object> producer) { consumer.Consume("value"); producer.Produce(); } }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Before();", "After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Flow.Run", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        Assert.Contains("ObjectConsumer.After", output);
        Assert.Contains("TextProducer.After", output);
    }

    private static Task<GitFixture> CreateAsync(string before, string after)
    {
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        return GitFixture.CreateAsync(new Scenario("generic-dispatch", "Constructed invariant contracts constrain possible source implementations.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = before },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []));
    }

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }
}
