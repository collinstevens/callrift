using Xunit;

namespace Callrift.Scenarios;

public sealed class FileLocalIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FileLocalTypesKeepTheirOwnCallers(bool workspace, bool interfaceDispatch)
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
        await using var fixture = await GitFixture.CreateAsync(new Scenario("file-local-identity", "Identically named file-local types bind to their own files and retain distinct callers.", before, after, []));
        string[] selection = workspace ? ["--project", "App.csproj"] : [];
        foreach (var format in new[] { "text", "md", "json" })
        {
            var output = await fixture.RunAsync(["diff", fixture.Before, fixture.After, .. selection, "--format", format]);
            Assert.True(output.StartsWith("exit: 0\n", StringComparison.Ordinal), output);
            Assert.Contains("First.Entry", output);
            Assert.Contains("Worker.Before", output);
            Assert.Contains("Worker.After", output);
            Assert.DoesNotContain("Second.Entry", output);
            Assert.DoesNotContain("duplicate-member", output);
            var other = await fixture.RunAsync(["tree", fixture.After, "--entry", "Second.Entry", .. selection, "--format", format]);
            Assert.True(other.StartsWith("exit: 0\n", StringComparison.Ordinal), other);
            Assert.Contains("Worker.Unrelated", other);
            Assert.DoesNotContain("Worker.After", other);
        }
    }
}
