using System.Text.Json;
using Callrift.Scenarios;
using Xunit;

namespace Callrift.Workspaces;

public sealed class AdditionalFileOrderTests
{
    [Fact]
    public async Task GeneratedCallsFollowCompilerInputOrderAcrossFreshWorkers()
    {
        var before = Sources();
        var after = new Dictionary<string, string>(before) { ["App/Inputs/B.txt"] = "C" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("additional-file-order",
            "A generator preserves evaluated additional-file order when only the final input changes.", before, after, []));
        foreach (var (files, expected) in new[] { (before, "Sink.Z();Sink.A();Sink.M();Sink.B();"), (after, "Sink.Z();Sink.A();Sink.M();Sink.C();") })
        {
            await fixture.WriteAsync(files);
            await WorkspaceFixture.RunAsync(fixture.Directory,
                ["build", "App/App.csproj", "--nologo", "-p:EmitCompilerGeneratedFiles=true", "-p:CompilerGeneratedFilesOutputPath=obj/generated"]);
            var generated = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Directory, "App/obj/generated"), "Flow.g.cs", SearchOption.AllDirectories));
            Assert.Contains(expected, await File.ReadAllTextAsync(generated));
        }

        string[] selection = ["diff", fixture.Before, fixture.After, "--project", "App/App.csproj", "--framework", "net11.0"];
        var json = await fixture.RunAsync([.. selection, "--format", "json"]);
        Assert.True(json.StartsWith("exit: 0\n", StringComparison.Ordinal), json);
        using var document = JsonDocument.Parse(json.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Run", root.GetProperty("label").GetString());
        var generatedCall = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal("Flow.Generated", generatedCall.GetProperty("label").GetString());
        var calls = generatedCall.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(["Sink.Z", "Sink.A", "Sink.M", "Sink.B"],
            calls.Where(call => call.GetProperty("before").ValueKind != JsonValueKind.Null).Select(call => call.GetProperty("label").GetString()));
        Assert.Equal(["Sink.Z", "Sink.A", "Sink.M", "Sink.C"],
            calls.Where(call => call.GetProperty("after").ValueKind != JsonValueKind.Null).Select(call => call.GetProperty("label").GetString()));
        foreach (var call in calls)
            Assert.Equal(call.GetProperty("label").GetString() switch { "Sink.B" => "removed", "Sink.C" => "added", _ => "unchanged" },
                call.GetProperty("change").GetString());

        Assert.Equal(json, await fixture.RunAsync([.. selection, "--format", "json", "--no-restore"]));
        foreach (var format in new[] { "text", "md" })
        {
            var rendered = await fixture.RunAsync([.. selection, "--format", format, "--no-restore"]);
            Assert.True(rendered.StartsWith("exit: 0\n", StringComparison.Ordinal), rendered);
            Assert.Contains("Sink.B", rendered);
            Assert.Contains("Sink.C", rendered);
            Assert.Equal(rendered, await fixture.RunAsync([.. selection, "--format", format, "--no-restore"]));
        }
    }

    private static Dictionary<string, string> Sources() => new()
    {
        ["App/App.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                <Import Project="Inputs.props" />
                <ItemGroup>
                    <AdditionalFiles Include="Inputs/M.txt;Inputs/B.txt" Condition="'$(TargetFramework)' == 'net11.0'" />
                    <AdditionalFiles Update="Inputs/*.txt" Prefix="Sink" />
                    <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="Prefix" />
                    <ProjectReference Include="../Generator/Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                </ItemGroup>
            </Project>
            """,
        ["App/Inputs.props"] = "<Project><ItemGroup><AdditionalFiles Include=\"Inputs/Z.txt;Inputs/A.txt\" /></ItemGroup></Project>",
        ["App/Inputs/Z.txt"] = "Z",
        ["App/Inputs/A.txt"] = "A",
        ["App/Inputs/M.txt"] = "M",
        ["App/Inputs/B.txt"] = "B",
        ["App/Flow.cs"] = "partial class Flow { public static void Run() => Generated(); } static class Sink { public static void Z() {} public static void A() {} public static void M() {} public static void B() {} public static void C() {} }",
        ["Generator/Generator.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"5.9.0\" /></ItemGroup></Project>",
        ["Generator/FlowGenerator.cs"] = """
            using System.Linq;
            using Microsoft.CodeAnalysis;

            [Generator]
            public sealed class FlowGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    context.RegisterSourceOutput(context.AdditionalTextsProvider.Collect().Combine(context.AnalyzerConfigOptionsProvider), (output, inputs) =>
                    {
                        var calls = string.Concat(inputs.Left.Select(file =>
                        {
                            inputs.Right.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.Prefix", out var prefix);
                            return prefix + "." + file.GetText(output.CancellationToken).ToString().Trim() + "();";
                        }));
                        output.AddSource("Flow.g.cs", "partial class Flow { public static void Generated() { " + calls + " } }");
                    });
                }
            }
            """
    };
}
