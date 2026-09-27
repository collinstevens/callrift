using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class VarianceCompatibilityTests
{
    [Theory]
    [InlineData("covariant")]
    [InlineData("contravariant")]
    [InlineData("boxing")]
    [InlineData("array")]
    [InlineData("array-rank")]
    [InlineData("generic-base")]
    [InlineData("user-defined")]
    [InlineData("array-elements")]
    [InlineData("dynamic")]
    [InlineData("native-integer")]
    [Trait("Layer", "Fast")]
    public Task IncompatibleVariantArgumentsDoNotCreatePaths(string kind) => VerifyIncompatibleVariantArgumentsDoNotCreatePaths(false, kind);

    [Theory]
    [InlineData("covariant")]
    [InlineData("contravariant")]
    [InlineData("boxing")]
    [InlineData("array")]
    [InlineData("array-rank")]
    [InlineData("generic-base")]
    [InlineData("user-defined")]
    [InlineData("array-elements")]
    [InlineData("dynamic")]
    [InlineData("native-integer")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceIncompatibleVariantArgumentsDoNotCreatePaths(string kind) => VerifyIncompatibleVariantArgumentsDoNotCreatePaths(true, kind);

    private static async Task VerifyIncompatibleVariantArgumentsDoNotCreatePaths(bool workspace, string kind)
    {
        var source = kind switch
        {
            "covariant" => """
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<string> { public string Produce() => "text"; }
                class Wrong : IProducer<object> { public object Produce() { Sink.Before(); return new object(); } }
                class Flow { public object Run(IProducer<string> producer) => producer.Produce(); }
                """,
            "contravariant" => """
                interface IConsumer<in T> { void Consume(T value); }
                class Wanted : IConsumer<object> { public void Consume(object value) {} }
                class Wrong : IConsumer<string> { public void Consume(string value) => Sink.Before(); }
                class Flow { public void Run(IConsumer<object> consumer) => consumer.Consume(new object()); }
                """,
            "boxing" => """
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<object> { public object Produce() => new object(); }
                class Wrong : IProducer<int> { public int Produce() { Sink.Before(); return 1; } }
                class Flow { public object Run(IProducer<object> producer) => producer.Produce(); }
                """,
            "array" => """
                using System.Collections.Generic;
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<string[]> { public string[] Produce() => []; }
                class Wrong : IProducer<int[]> { public int[] Produce() { Sink.Before(); return []; } }
                class Flow { public object Run(IProducer<IList<object>> producer) => producer.Produce(); }
                """,
            "array-rank" => """
                using System.Collections.Generic;
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<string[]> { public string[] Produce() => []; }
                class Wrong : IProducer<string[,]> { public string[,] Produce() { Sink.Before(); return new string[1, 1]; } }
                class Flow { public object Run(IProducer<IList<object>> producer) => producer.Produce(); }
                """,
            "generic-base" => """
                using System.Collections.Generic;
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<List<string>> { public List<string> Produce() => []; }
                class Wrong : IProducer<List<int>> { public List<int> Produce() { Sink.Before(); return []; } }
                class Flow { public object Run(IProducer<IEnumerable<object>> producer) => producer.Produce(); }
                """,
            "user-defined" => """
                class Animal {}
                class Converted { public static implicit operator Animal(Converted value) => new Animal(); }
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<Animal> { public Animal Produce() => new Animal(); }
                class Wrong : IProducer<Converted> { public Converted Produce() { Sink.Before(); return new Converted(); } }
                class Flow { public object Run(IProducer<Animal> producer) => producer.Produce(); }
                """,
            "array-elements" => """
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<string[,]> { public string[,] Produce() => new string[1, 1]; }
                class Wrong : IProducer<int[,]> { public int[,] Produce() { Sink.Before(); return new int[1, 1]; } }
                class Flow { public object Run(IProducer<object[,]> producer) => producer.Produce(); }
                """,
            "dynamic" => """
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<object> { public object Produce() => new object(); }
                class Wrong : IProducer<int> { public int Produce() { Sink.Before(); return 1; } }
                class Flow { public object Run(IProducer<dynamic> producer) => producer.Produce(); }
                """,
            "native-integer" => """
                interface IProducer<out T> { T Produce(); }
                class Wanted : IProducer<nint> { public nint Produce() => 1; }
                class Wrong : IProducer<int> { public int Produce() { Sink.Before(); return 1; } }
                class Flow { public System.IntPtr Run(IProducer<System.IntPtr> producer) => producer.Produce(); }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        source += "\nstatic class Sink { public static void Before() {} public static void After() {} }";
        await using var fixture = await CreateAsync(source, source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal), workspace);
        using var diff = Parse(await fixture.DiffAsync());
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        var changed = Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal(kind == "contravariant" ? "Wrong.Consume" : "Wrong.Produce", changed.GetProperty("label").GetString());
        var tree = await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] });
        using var document = Parse(tree);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.DoesNotContain("Wrong", tree);
        Assert.Contains("Wanted", tree);
        using var reach = Parse(await fixture.QueryAsync(new DiffOptions { Entries = ["Flow.Run"] }, target: "Sink.After"));
        Assert.Empty(reach.RootElement.GetProperty("paths").EnumerateArray());
        Assert.False(reach.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData("App")]
    [Trait("Layer", "Fast")]
    public Task InheritanceAndNestedVarianceRemainReachable(string assemblyName) => VerifyInheritanceAndNestedVarianceRemainReachable(false, assemblyName);

    [Theory]
    [InlineData("App")]
    [InlineData("array")]
    [Trait("Layer", "Integration")]
    public Task WorkspaceInheritanceAndNestedVarianceRemainReachable(string assemblyName) => VerifyInheritanceAndNestedVarianceRemainReachable(true, assemblyName);

    private static async Task VerifyInheritanceAndNestedVarianceRemainReachable(bool workspace, string assemblyName)
    {
        const string source = """
            using System.Collections.Generic;
            class Animal {}
            class Dog : Animal {}
            interface IProducer<out T> { T Produce(); }
            interface IConsumer<in T> { void Consume(T value); }
            class DogProducer : IProducer<Dog> { public Dog Produce() { Sink.Before(); return new Dog(); } }
            class AnimalConsumer : IConsumer<Animal> { public void Consume(Animal value) => Sink.Before(); }
            class NestedProducer : IProducer<IEnumerable<Dog>> { public IEnumerable<Dog> Produce() { Sink.Before(); return []; } }
            class Flow {
                public void Run(IProducer<Animal> producer, IConsumer<Dog> consumer, IProducer<IEnumerable<Animal>> nested) {
                    producer.Produce();
                    consumer.Consume(new Dog());
                    nested.Produce();
                }
            }
            static class Sink { public static void Before() {} public static void After() {} }
            """;
        await using var fixture = await CreateAsync(source, source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal), workspace, assemblyName);
        var output = await fixture.DiffAsync();
        using var document = Parse(output);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Flow.Run", Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray()).GetProperty("label").GetString());
        Assert.Contains("DogProducer.Produce", output);
        Assert.Contains("AnimalConsumer.Consume", output);
        Assert.Contains("NestedProducer.Produce", output);
        Assert.Contains("Sink.After", output);
        var producers = document.RootElement.GetProperty("trees")[0].GetProperty("children").EnumerateArray()
            .Where(n => n.GetProperty("after").GetProperty("symbolId").GetString()!.EndsWith("::IProducer<T>.Produce()", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, producers.Length);
        Assert.EndsWith("::DogProducer.Produce()", Assert.Single(producers[0].GetProperty("after").GetProperty("targetIds").EnumerateArray()).GetString());
        Assert.EndsWith("::NestedProducer.Produce()", Assert.Single(producers[1].GetProperty("after").GetProperty("targetIds").EnumerateArray()).GetString());
    }

    private static Task<AnalysisFixture> CreateAsync(string before, string after, bool workspace, string assemblyName = "App")
    {
        var project = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><AssemblyName>{assemblyName}</AssemblyName></PropertyGroup></Project>";
        return AnalysisFixture.CreateAsync(new Scenario("variance-compatibility", "Variant arguments require reference conversions in the declared direction.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = before },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []), workspace);
    }

    private static JsonDocument Parse(string output)
    {
        return JsonDocument.Parse(output);
    }
}
