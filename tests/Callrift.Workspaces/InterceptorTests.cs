using System.Text.Json;
using Callrift.Scenarios;
using Xunit;

namespace Callrift.Workspaces;

public sealed class InterceptorTests
{
    [Fact]
    public async Task GeneratedInterceptorsFollowReplacementBodiesWithRepeatableIdentities()
    {
        var before = Sources();
        var after = new Dictionary<string, string>(before)
        {
            ["Generator/CallGenerator.cs"] = before["Generator/CallGenerator.cs"].Replace("Sink.Before();", "Sink.After();", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("interceptor", "A generated interceptor replaces an unchanged call and keeps a stable graph despite private generated names.", before, after, []));
        string[] selection = ["--project", "App/App.csproj", "--framework", "net11.0"];
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. selection, "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray());
        Assert.Equal("Flow.Entry", root.GetProperty("label").GetString());
        Assert.Contains("Sink.Before", output);
        Assert.Contains("Sink.After", output);
        Assert.DoesNotContain("Sink.Fallback", output);
        var repeated = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. selection, "--format", "json", "--no-restore"]);
        Assert.Equal(output, repeated);
        foreach (var format in new[] { "text", "md", "json" })
        {
            var tree = await fixture.RunAsync(["tree", fixture.After, "--entry", "Flow.Entry", .. selection, "--format", format, "--no-restore"]);
            Assert.True(tree.StartsWith("exit: 0\n", StringComparison.Ordinal), tree);
            Assert.Contains("Sink.After", tree);
            Assert.DoesNotContain("Sink.Fallback", tree);
            var reach = await fixture.RunAsync(["reach", fixture.After, "--entry", "Flow.Entry", "--to", "Sink.After", .. selection, "--format", format, "--no-restore"]);
            Assert.True(reach.StartsWith("exit: 0\n", StringComparison.Ordinal), reach);
            Assert.Contains("Flow.Entry", reach);
            Assert.Contains("Sink.After", reach);
        }
    }

    [Fact]
    public async Task InsertingAnInterceptedCallPreservesTheExistingCall()
    {
        var before = Sources();
        before["App/Flow.cs"] = "public static class Flow { public static void Entry() { Original.Run(\"keep\"); } } public static class Original { public static void Run(string tag) => Sink.Fallback(); } public static class Sink { public static void Kept() {} public static void Inserted() {} public static void Fallback() {} }";
        before["Generator/CallGenerator.cs"] = before["Generator/CallGenerator.cs"]
            .Replace("static (syntax, _) => syntax.SemanticModel.GetInterceptableLocation((InvocationExpressionSyntax)syntax.Node)?.GetInterceptsLocationAttributeSyntax()",
                "static (syntax, _) => (Attribute: syntax.SemanticModel.GetInterceptableLocation((InvocationExpressionSyntax)syntax.Node)?.GetInterceptsLocationAttributeSyntax(), Arguments: ((InvocationExpressionSyntax)syntax.Node).ArgumentList.ToString())", StringComparison.Ordinal)
            .Replace("if (attribute is null) return;", "if (attribute.Attribute is null) return; var target = attribute.Arguments.Contains(\"\\\"keep\\\"\") ? \"Kept\" : \"Inserted\";", StringComparison.Ordinal)
            .Replace("\"Interceptors.g.cs\"", "\"Interceptors-\" + target + \".g.cs\"", StringComparison.Ordinal)
            .Replace("\" { \" + attribute + \" public static void Invoke() => Sink.Before(); } }\"",
                "\" { \" + attribute.Attribute + \" public static void Invoke(string tag) => Sink.\" + target + \"(); } }\"", StringComparison.Ordinal);
        var after = new Dictionary<string, string>(before)
        {
            ["App/Flow.cs"] = before["App/Flow.cs"].Replace("Original.Run(\"keep\");", "Original.Run(\"new\"); Original.Run(\"keep\");", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("interceptor-insertion", "Adding a distinct intercepted call preserves the existing replacement and marks only the new call as added.", before, after, []));
        var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, "--project", "App/App.csproj", "--framework", "net11.0", "--format", "json"]);
        Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
        using var document = JsonDocument.Parse(output.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0]);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        var root = Assert.Single(document.RootElement.GetProperty("trees").EnumerateArray(), node => node.GetProperty("label").GetString() == "Flow.Entry");
        var nodes = Descendants(root).ToArray();
        var kept = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Sink.Kept");
        Assert.Equal("unchanged", kept.GetProperty("change").GetString());
        var inserted = Assert.Single(nodes, node => node.GetProperty("label").GetString() == "Sink.Inserted");
        Assert.Equal("added", inserted.GetProperty("change").GetString());
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement node)
    {
        yield return node;
        foreach (var child in node.GetProperty("children").EnumerateArray())
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static Dictionary<string, string> Sources()
    {
        return new Dictionary<string, string>
        {
            ["App/App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><InterceptorsNamespaces>Generated</InterceptorsNamespaces></PropertyGroup><ItemGroup><ProjectReference Include=\"../Generator/Generator.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" /></ItemGroup></Project>",
            ["App/Flow.cs"] = "public static class Flow { public static void Entry() => Original.Run(); } public static class Original { public static void Run() => Sink.Fallback(); } public static class Sink { public static void Before() {} public static void After() {} public static void Fallback() {} }",
            ["Generator/Generator.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework><LangVersion>latest</LangVersion><NoWarn>RSEXPERIMENTAL002</NoWarn></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"5.9.0\" /></ItemGroup></Project>",
            ["Generator/CallGenerator.cs"] = """
                using System;
                using Microsoft.CodeAnalysis;
                using Microsoft.CodeAnalysis.CSharp;
                using Microsoft.CodeAnalysis.CSharp.Syntax;

                [Generator]
                public sealed class CallGenerator : IIncrementalGenerator
                {
                    public void Initialize(IncrementalGeneratorInitializationContext context)
                    {
                        var locations = context.SyntaxProvider.CreateSyntaxProvider(
                            static (node, _) => node is InvocationExpressionSyntax invocation && invocation.Expression.ToString() == "Original.Run",
                            static (syntax, _) => syntax.SemanticModel.GetInterceptableLocation((InvocationExpressionSyntax)syntax.Node)?.GetInterceptsLocationAttributeSyntax());
                        context.RegisterSourceOutput(locations, static (output, attribute) =>
                        {
                            if (attribute is null) return;
                            output.AddSource("Interceptors.g.cs",
                                "namespace Generated { file static class Interceptor_" + Guid.NewGuid().ToString("N") +
                                " { " + attribute + " public static void Invoke() => Sink.Before(); } }" +
                                "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Method)] file sealed class InterceptsLocationAttribute : System.Attribute { public InterceptsLocationAttribute(int version, string data) {} } }");
                        });
                    }
                }
                """
        };
    }
}
