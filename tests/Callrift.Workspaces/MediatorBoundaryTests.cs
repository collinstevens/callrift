using System.Text.Json;
using Callrift.Scenarios;
using Xunit;

namespace Callrift.Workspaces;

public sealed class MediatorBoundaryTests
{
    [Fact]
    public async Task PackageHandlerDispatchAndOpaqueSendKeepPartialCliCoverage()
    {
        var before = new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"MediatR\" Version=\"13.1.0\" /></ItemGroup></Project>",
            ["Flow.cs"] = """
                using System.Threading;
                using System.Threading.Tasks;
                using MediatR;

                public sealed class Request : IRequest<int> { }

                public sealed class Handler : IRequestHandler<Request, int>
                {
                    public Task<int> Handle(Request request, CancellationToken cancellationToken) => Task.FromResult(Sink.Before());
                }

                public static class Flow
                {
                    public static Task<int> ThroughContract(IRequestHandler<Request, int> handler, Request request, CancellationToken cancellationToken)
                        => handler.Handle(request, cancellationToken);

                    public static Task<int> ThroughMediator(ISender sender, Request request, CancellationToken cancellationToken)
                        => sender.Send<int>(request, cancellationToken);
                }

                public static class Sink
                {
                    public static int Before() => 1;
                    public static int After() => 2;
                }
                """
        };
        var after = new Dictionary<string, string>(before)
        {
            ["Flow.cs"] = before["Flow.cs"].Replace("Sink.Before()", "Sink.After()", StringComparison.Ordinal)
        };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("mediator-boundary",
            "A pinned package handler contract dispatches to source; opaque mediator Send does not model handler selection and strict output retains partial coverage.", before, after, []));
        var restored = false;
        foreach (var command in new[] { "diff", "tree", "reach" })
            foreach (var entry in command == "diff" ? new[] { "" } : new[] { "Flow.ThroughContract", "Flow.ThroughMediator" })
                foreach (var format in new[] { "json", "text", "md" })
                {
                    string[] revisions = command == "diff" ? [fixture.Before, fixture.After] : [fixture.After];
                    string[] selection = entry.Length == 0 ? [] : ["--entry", entry];
                    string[] target = command == "reach" ? ["--to", "Sink.After"] : [];
                    string[] restore = restored ? ["--no-restore"] : [];
                    var result = await fixture.RunAsync([command, .. revisions, .. selection, .. target, "--project", "App.csproj",
                        "--format", format, "--externals", "--strict", "--depth", "10", .. restore]);
                    Assert.StartsWith("exit: 2\n", result);
                    restored = true;
                    var output = result.Split("stdout:\n", StringSplitOptions.None)[1].Split("stderr:\n", StringSplitOptions.None)[0];
                    var error = result.Split("stderr:\n", StringSplitOptions.None)[1];
                    Assert.Contains("analysis is partial; inspect JSON coverage and diagnostics.", error);
                    var mediated = entry == "Flow.ThroughMediator";
                    if (format == "json")
                    {
                        using var document = JsonDocument.Parse(output);
                        var root = document.RootElement;
                        Assert.Equal("msbuild", root.GetProperty("analysis").GetProperty("mode").GetString());
                        Assert.Equal("partial", root.GetProperty("analysis").GetProperty("status").GetString());
                        Assert.Contains("no-framework-dispatch-plugins", root.GetProperty("analysis").GetProperty("limitations").EnumerateArray().Select(item => item.GetString()));
                        Assert.False(root.GetProperty("truncated").GetBoolean());
                        Assert.All(root.GetProperty("diagnostics").EnumerateArray(), diagnostic => Assert.Equal("workspace-warning", diagnostic.GetProperty("code").GetString()));
                        var nodes = root.GetProperty(command == "reach" ? "paths" : "trees").EnumerateArray();
                        if (mediated && command == "reach") Assert.Empty(nodes);
                        else Assert.NotEmpty(nodes);
                        if (mediated && command == "tree")
                        {
                            var call = Assert.Single(Assert.Single(root.GetProperty("trees").EnumerateArray()).GetProperty("children").EnumerateArray());
                            Assert.Equal("metadata", call.GetProperty("after").GetProperty("origin").GetString());
                            Assert.Contains("MediatR.ISender.Send", call.GetProperty("after").GetProperty("symbolId").GetString());
                            Assert.Empty(call.GetProperty("children").EnumerateArray());
                        }
                    }
                    else if (mediated && command == "reach") Assert.Contains("No call paths found.", output);
                    if (mediated)
                    {
                        Assert.DoesNotContain("Handler.Handle", output);
                        Assert.DoesNotContain("Sink.After", output);
                        if (command == "tree") Assert.Contains("sender.Send<int>", output);
                    }
                    else
                    {
                        Assert.Contains("Flow.ThroughContract", output);
                        Assert.Contains("Handler.Handle", output);
                        Assert.Contains("Sink.After", output);
                        Assert.DoesNotContain("Flow.ThroughMediator", output);
                        if (command == "diff") Assert.Contains("Sink.Before", output);
                    }
                }
    }
}
