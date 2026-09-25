using System.Text.Json;
using Xunit;

namespace Callrift.Scenarios;

public sealed class VarianceCompatibilityTests
{
    [Theory]
    [InlineData(false, "covariant")]
    [InlineData(true, "covariant")]
    [InlineData(false, "contravariant")]
    [InlineData(true, "contravariant")]
    [InlineData(false, "boxing")]
    [InlineData(true, "boxing")]
    [InlineData(false, "array")]
    [InlineData(true, "array")]
    [InlineData(false, "array-rank")]
    [InlineData(true, "array-rank")]
    [InlineData(false, "generic-base")]
    [InlineData(true, "generic-base")]
    [InlineData(false, "user-defined")]
    [InlineData(true, "user-defined")]
    [InlineData(false, "array-elements")]
    [InlineData(true, "array-elements")]
    [InlineData(false, "dynamic")]
    [InlineData(true, "dynamic")]
    [InlineData(false, "native-integer")]
    [InlineData(true, "native-integer")]
    public async Task IncompatibleVariantArgumentsDoNotCreatePaths(bool workspace, string kind)
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
        await using var fixture = await CreateAsync(source, source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal));
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        using var diff = Parse(await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]));
        Assert.Empty(diff.RootElement.GetProperty("diagnostics").EnumerateArray());
        var changed = Assert.Single(diff.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal(kind == "contravariant" ? "Wrong.Consume" : "Wrong.Produce", changed.GetProperty("label").GetString());
        var tree = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Run", .. mode, "--format", "json"]);
        using var document = Parse(tree);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.DoesNotContain("Wrong", tree);
        Assert.Contains("Wanted", tree);
        using var reach = Parse(await fixture.RunAsync(["reach", fixture.After, "--entry", "Flow.Run", "--to", "Sink.After", .. mode, "--format", "json"]));
        Assert.Empty(reach.RootElement.GetProperty("paths").EnumerateArray());
        Assert.False(reach.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData(false, "App")]
    [InlineData(true, "App")]
    [InlineData(true, "array")]
    public async Task InheritanceAndNestedVarianceRemainReachable(bool workspace, string assemblyName)
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
        await using var fixture = await CreateAsync(source, source.Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal), assemblyName);
        string[] mode = workspace ? ["--project", "App.csproj"] : [];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. mode, "--format", "json"]);
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

    private static Task<GitFixture> CreateAsync(string before, string after, string assemblyName = "App")
    {
        var project = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><AssemblyName>{assemblyName}</AssemblyName></PropertyGroup></Project>";
        return GitFixture.CreateAsync(new Scenario("variance-compatibility", "Variant arguments require reference conversions in the declared direction.",
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = before },
            new Dictionary<string, string> { ["App.csproj"] = project, ["Flow.cs"] = after }, []));
    }

    private static JsonDocument Parse(string output)
    {
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        return JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
    }
}
