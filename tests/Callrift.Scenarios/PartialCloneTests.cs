using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Callrift.Core;
using Xunit;

namespace Callrift.Scenarios;

public sealed class PartialCloneTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task SelectedBlobsAreFetchedTogether(bool preserveBytes, bool fallbackRemote, bool concurrent)
    {
        var before = new Dictionary<string, string>
        {
            ["A.cs"] = "class A { public int Value() => 1; }",
            ["nested/B.cs"] = "class B { public string Value() => \"second\"; }",
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>",
            ["assets/data.bin"] = "\0\u0001\u007f\u00ff"
        };
        var after = new Dictionary<string, string>(before) { ["A.cs"] = "class A { public int Value() => 2; }" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("partial-clone", "Selected inputs are hydrated together without unrelated assets.", before, after, []));
        await fixture.Git("config", "uploadpack.allowFilter", "true");
        var clone = Path.Combine(fixture.Directory, "filtered");
        await fixture.Git("clone", "--filter=blob:none", "--no-checkout", "--no-local", "--origin", "archive", new Uri(fixture.Directory + Path.DirectorySeparatorChar).AbsoluteUri, clone);
        if (fallbackRemote)
        {
            await GitRepository.RunAsync(clone, ["remote", "add", "unavailable", new Uri(Path.Combine(fixture.Directory, "missing") + Path.DirectorySeparatorChar).AbsoluteUri]);
            await GitRepository.RunAsync(clone, ["config", "remote.unavailable.promisor", "true"]);
            await GitRepository.RunAsync(clone, ["config", "extensions.partialClone", "archive"]);
        }
        var trace = Path.Combine(fixture.Directory, "fetch-trace.jsonl");
        var previousTrace = Environment.GetEnvironmentVariable("GIT_TRACE2_EVENT");
        try
        {
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", trace);
            var repository = new GitRepository(clone);
            var afterRead = repository.ReadSnapshotAsync(fixture.After, allFiles: preserveBytes);
            if (concurrent)
            {
                var beforeRead = repository.ReadSnapshotAsync(fixture.Before);
                await Task.WhenAll(beforeRead, afterRead);
                Assert.Equal("class A { public int Value() => 1; }", (await beforeRead).Files.Single(f => f.Path == "A.cs").Content);
            }
            var snapshot = await afterRead;
            Assert.Equal(preserveBytes ? ["A.cs", "App.csproj", "assets/data.bin", "nested/B.cs"] : new[] { "A.cs", "App.csproj", "nested/B.cs" }, snapshot.Files.Select(f => f.Path));
            Assert.Equal("class A { public int Value() => 2; }", snapshot.Files.Single(f => f.Path == "A.cs").Content);
            Assert.Equal("class B { public string Value() => \"second\"; }", snapshot.Files.Single(f => f.Path == "nested/B.cs").Content);
            if (preserveBytes)
                Assert.Equal(Encoding.UTF8.GetBytes("\0\u0001\u007f\u00ff"), snapshot.Files.Single(f => f.Path == "assets/data.bin").RawBytes);
            else
            {
                var asset = (await fixture.Git("rev-parse", fixture.After + ":assets/data.bin")).Trim();
                await Assert.ThrowsAsync<InvalidOperationException>(() => GitRepository.RunAsync(clone, ["--no-lazy-fetch", "cat-file", "-e", asset]));
            }
            var fetches = CountFetches(trace);
            Assert.InRange(fetches, 1, fallbackRemote || concurrent ? 2 : 1);
            await repository.ReadSnapshotAsync(fixture.After, allFiles: preserveBytes);
            Assert.Equal(fetches, CountFetches(trace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", previousTrace);
        }
    }

    [Fact]
    public async Task MixedAvailabilityPreservesOrderAndRepeatedBlobs()
    {
        var before = new Dictionary<string, string> { ["A.cs"] = "class A {}", ["B.cs"] = "class B {}", ["C.cs"] = "class C {}" };
        var after = new Dictionary<string, string> { ["A.cs"] = "class A { int Value = 1; }", ["B.cs"] = "class B { int Value = 2; }", ["C.cs"] = "class C { int Value = 3; }" };
        await using var fixture = await GitFixture.CreateAsync(new Scenario("mixed-blobs", "Partial availability preserves caller order and repeated blob requests.", before, after, []));
        await fixture.Git("config", "uploadpack.allowFilter", "true");
        var clone = Path.Combine(fixture.Directory, "filtered");
        await fixture.Git("clone", "--filter=blob:none", "--no-checkout", "--no-local", new Uri(fixture.Directory + Path.DirectorySeparatorChar).AbsoluteUri, clone);
        var repository = new GitRepository(clone);
        var ids = (await repository.ListEntriesAsync(fixture.After)).ToDictionary(e => e.Path, e => e.ObjectId);
        await GitRepository.RunAsync(clone, ["cat-file", "blob", ids["B.cs"]]);
        GitEntry[] requests = [new("third.cs", ids["C.cs"]), new("second.cs", ids["B.cs"]), new("first.cs", ids["A.cs"]), new("again.cs", ids["C.cs"])];
        var trace = Path.Combine(fixture.Directory, "fetch-trace.jsonl");
        var previousTrace = Environment.GetEnvironmentVariable("GIT_TRACE2_EVENT");
        try
        {
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", trace);
            var files = await repository.ReadBlobsAsync(requests);
            Assert.Equal(["third.cs", "second.cs", "first.cs", "again.cs"], files.Select(f => f.Path));
            Assert.Equal(["class C { int Value = 3; }", "class B { int Value = 2; }", "class A { int Value = 1; }", "class C { int Value = 3; }"], files.Select(f => f.Content));
            Assert.Equal(1, CountFetches(trace));
            Assert.Equal(files, await repository.ReadBlobsAsync(requests));
            Assert.Equal(1, CountFetches(trace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", previousTrace);
        }
    }

    [Fact]
    public async Task ObjectExpressionsRemainSupported()
    {
        await using var fixture = await CreateSmallFixtureAsync();
        var files = await new GitRepository(fixture.Directory).ReadBlobsAsync([new("renamed.cs", fixture.After + ":A.cs")]);
        var file = Assert.Single(files);
        Assert.Equal("renamed.cs", file.Path);
        Assert.Equal("class A { void Run() {} }", file.Content);
    }

    [Fact]
    public async Task ExplicitlyDisabledLazyFetchingIsRespected()
    {
        await using var fixture = await CreateSmallFixtureAsync();
        await fixture.Git("config", "uploadpack.allowFilter", "true");
        var clone = Path.Combine(fixture.Directory, "filtered");
        await fixture.Git("clone", "--filter=blob:none", "--no-checkout", "--no-local", new Uri(fixture.Directory + Path.DirectorySeparatorChar).AbsoluteUri, clone);
        var trace = Path.Combine(fixture.Directory, "fetch-trace.jsonl");
        var previousTrace = Environment.GetEnvironmentVariable("GIT_TRACE2_EVENT");
        var previousNoFetch = Environment.GetEnvironmentVariable("GIT_NO_LAZY_FETCH");
        try
        {
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", trace);
            Environment.SetEnvironmentVariable("GIT_NO_LAZY_FETCH", "1");
            await Assert.ThrowsAsync<InvalidOperationException>(() => new GitRepository(clone).ReadSnapshotAsync(fixture.After));
            Assert.Equal(0, CountFetches(trace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_NO_LAZY_FETCH", previousNoFetch);
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", previousTrace);
        }
    }

    [Fact]
    public async Task CancellationStopsAnActiveFetch()
    {
        await using var fixture = await CreateSmallFixtureAsync();
        await fixture.Git("config", "uploadpack.allowFilter", "true");
        var clone = Path.Combine(fixture.Directory, "filtered");
        await fixture.Git("clone", "--filter=blob:none", "--no-checkout", "--no-local", new Uri(fixture.Directory + Path.DirectorySeparatorChar).AbsoluteUri, clone);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await GitRepository.RunAsync(clone, ["remote", "set-url", "origin", $"git://127.0.0.1:{port}/pending"]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = new CancellationTokenSource();
        var read = new GitRepository(clone).ReadSnapshotAsync(fixture.After, cancellationToken: cancellation.Token);
        try
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(timeout.Token));
        }
        finally
        {
            cancellation.Cancel();
            try { await read; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task MissingOrdinaryObjectsRemainErrors()
    {
        await using var fixture = await CreateSmallFixtureAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GitRepository(fixture.Directory)
            .ReadBlobsAsync([new GitEntry("missing.cs", new string('0', 40))]));
        Assert.Contains("missing.cs", error.Message);
    }

    [Fact]
    public async Task CancelledReadDoesNotStartFetching()
    {
        await using var fixture = await CreateSmallFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitRepository(fixture.Directory)
            .ReadBlobsAsync([new GitEntry("missing.cs", new string('0', 40))], cancellation.Token));
    }

    private static Task<GitFixture> CreateSmallFixtureAsync() => GitFixture.CreateAsync(new Scenario("git-inputs", "Git input failures remain explicit.",
        new Dictionary<string, string> { ["A.cs"] = "class A {}" }, new Dictionary<string, string> { ["A.cs"] = "class A { void Run() {} }" }, []));

    private static int CountFetches(string path) => File.ReadLines(path).Count(line =>
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return root.TryGetProperty("event", out var kind) && kind.GetString() == "cmd_name"
            && root.TryGetProperty("name", out var name) && name.GetString() == "fetch";
    });
}
