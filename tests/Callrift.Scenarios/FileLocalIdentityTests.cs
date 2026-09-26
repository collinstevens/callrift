using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class FileLocalIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Fast")]
    public Task FileLocalTypesKeepTheirOwnCallers(bool interfaceDispatch) => VerifyFileLocalCallers(false, interfaceDispatch);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public Task WorkspaceFileLocalTypesKeepTheirOwnCallers(bool interfaceDispatch) => VerifyFileLocalCallers(true, interfaceDispatch);

    private static async Task VerifyFileLocalCallers(bool workspace, bool interfaceDispatch)
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["First.cs"] = "public static class First { public static void Entry() => Worker.Run(); } file static class Worker { public static void Run() => Before(); static void Before() {} static void After() {} }",
            ["Second.cs"] = "public static class Second { public static void Entry() => Worker.Run(); } file static class Worker { public static void Run() => Unrelated(); static void Unrelated() {} }"
        };
        if (interfaceDispatch)
        {
            before["Contract.cs"] = "public interface IWorker { void Run(); }";
            before["First.cs"] = "public static class First { public static void Entry() { var worker = new Worker(); ((IWorker)worker).Run(); } } file class Worker : IWorker { public void Run() => Before(); void Before() {} void After() {} }";
            before["Second.cs"] = "public static class Second { public static void Entry() { var worker = new Worker(); ((IWorker)worker).Run(); } } file class Worker : IWorker { public void Run() => Unrelated(); void Unrelated() {} }";
        }
        var after = new Dictionary<string, string>(before)
        {
            ["First.cs"] = before["First.cs"].Replace("=> Before();", "=> After();", StringComparison.Ordinal)
        };
        var scenario = new Scenario("file-local-identity", "Identically named file-local types bind to their own files and retain distinct callers.", before, after, []);
        await using var fixture = workspace ? await AnalysisFixture.CreateWorkspaceCliAsync(scenario)
            : await AnalysisFixture.CreateAsync(scenario, workspace: false);
        var diffs = await fixture.DiffFormatsAsync(new DiffOptions());
        var trees = await fixture.QueryFormatsAsync(new DiffOptions { Entries = ["Second.Entry"] });
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = diffs[format];
            Assert.Contains("First.Entry", output);
            Assert.Contains("Worker.Before", output);
            Assert.Contains("Worker.After", output);
            Assert.DoesNotContain("Second.Entry", output);
            Assert.DoesNotContain("duplicate-member", output);
            var other = trees[format];
            Assert.Contains("Worker.Unrelated", other);
            Assert.DoesNotContain("Worker.After", other);
        }
    }
}
