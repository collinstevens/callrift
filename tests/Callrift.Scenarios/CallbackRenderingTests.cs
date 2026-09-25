using Xunit;

namespace Callrift.Scenarios;

public sealed class CallbackRenderingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DifferentCallbackBodiesAreNotRepeatedExpansions(bool workspace, bool inline)
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
        await using var fixture = await GitFixture.CreateAsync(new Scenario("different-callbacks", "Repeated receiving methods retain distinct callback bodies in every format.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("Flow.A", output);
            Assert.Contains("Flow.B", output);
            Assert.Contains("Flow.BeforeA", output);
            Assert.Contains("Flow.BeforeB", output);
            Assert.Contains("Flow.AfterA", output);
            Assert.Contains("Flow.AfterB", output);
            Assert.DoesNotContain("as above", output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocationsKeepRepeatedCallSitesVisible(bool workspace)
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
        await using var fixture = await GitFixture.CreateAsync(new Scenario("callback-locations", "Distinct callback call sites remain visible when locations are requested.", before, after, []));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", format, "--locs"]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("Flow.Consume [Flow.cs:6]", output);
            Assert.Contains("Flow.Consume [Flow.cs:7]", output);
            Assert.DoesNotContain("×2", output);
        }
    }
}
