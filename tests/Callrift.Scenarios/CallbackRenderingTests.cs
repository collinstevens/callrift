using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class CallbackRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public async Task DifferentCallbackBodiesAreNotRepeatedExpansions(bool inline)
    {
        var (before, after) = await SourceFixture.AnalyzeAsync(DifferentCallbacks(inline));
        var options = new DiffOptions();
        var result = CallriftService.Compare(before, after, options);
        foreach (var output in new[] { DiffRenderer.Render(result, options), DiffRenderer.Render(result, options, markdown: true), JsonRenderer.Render(result) })
            AssertDifferentCallbacks(output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task WorkspaceDifferentCallbackBodiesAreNotRepeatedExpansions(bool inline)
    {
        await using var fixture = inline
            ? await AnalysisFixture.CreateAsync(DifferentCallbacks(inline), workspace: true)
            : await AnalysisFixture.CreateWorkspaceCliAsync(DifferentCallbacks(inline));
        foreach (var output in (await fixture.DiffFormatsAsync(new DiffOptions())).Values)
            AssertDifferentCallbacks(output);
    }

    [Fact]
    [Trait("Layer", "Fast")]
    public async Task LocationsKeepRepeatedCallSitesVisible()
    {
        var (before, after) = await SourceFixture.AnalyzeAsync(CallbackLocations());
        var options = new DiffOptions { Locations = true };
        var result = CallriftService.Compare(before, after, options);
        foreach (var markdown in new[] { false, true })
            AssertLocations(DiffRenderer.Render(result, options, markdown));
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task WorkspaceLocationsKeepRepeatedCallSitesVisible()
    {
        await using var fixture = await AnalysisFixture.CreateAsync(CallbackLocations(), workspace: true);
        var outputs = await fixture.DiffFormatsAsync(new DiffOptions { Locations = true });
        foreach (var format in new[] { "text", "md" })
            AssertLocations(outputs[format]);
    }

    private static void AssertDifferentCallbacks(string output)
    {
        Assert.Contains("Flow.A", output);
        Assert.Contains("Flow.B", output);
        Assert.Contains("Flow.BeforeA", output);
        Assert.Contains("Flow.BeforeB", output);
        Assert.Contains("Flow.AfterA", output);
        Assert.Contains("Flow.AfterB", output);
        Assert.DoesNotContain("as above", output);
    }

    private static void AssertLocations(string output)
    {
        Assert.Contains("Flow.Consume [Flow.cs:6]", output);
        Assert.Contains("Flow.Consume [Flow.cs:7]", output);
        Assert.DoesNotContain("×2", output);
    }

    private static Scenario DifferentCallbacks(bool inline)
    {
        var source = """
            using System;
            class Flow
            {
                public void Run() { Consume(A); Consume(B); }
                void Consume(Action callback) {}
                void A() { BeforeA(); }
                void B() { BeforeB(); }
                void BeforeA() {}
                void BeforeB() {}
                void AfterA() {}
                void AfterB() {}
            }
            """;
        if (inline) source = source.Replace("Consume(A); Consume(B);", "Consume(() => A()); Consume(() => B());", StringComparison.Ordinal);
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = source
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Flow.cs"] = source.Replace("BeforeA();", "AfterA();", StringComparison.Ordinal).Replace("BeforeB();", "AfterB();", StringComparison.Ordinal)
        };
        return new Scenario("different-callbacks", "Repeated receiving methods retain distinct callback bodies in every format.", before, after, []);
    }

    private static Scenario CallbackLocations()
    {
        const string source = """
            using System;
            class Flow
            {
                public void Run()
                {
                    Consume(A);
                    Consume(A);
                }
                void Consume(Action callback) {}
                void A() { Before(); }
                void Before() {}
                void After() {}
            }
            """;
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Flow.cs"] = source
        };
        var after = new Dictionary<string, string>(before) { ["Flow.cs"] = source.Replace("Before();", "After();", StringComparison.Ordinal) };
        return new Scenario("callback-locations", "Distinct callback call sites remain visible when locations are requested.", before, after, []);
    }
}
