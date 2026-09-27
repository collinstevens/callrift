using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AspNetUploadStreamExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.True(result.Truncated);
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Equal(22752, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(82, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
        Assert.Equal(focused ? 12 : 1296, result.Trees.Count);
        var nodes = Descendants(result.Trees).ToArray();
        Assert.DoesNotContain(nodes.SelectMany(node => new[] { node.Before?.Definition, node.After?.Definition }),
            location => location?.Path.StartsWith("src/SignalR/server/SignalR/test/", StringComparison.Ordinal) == true);
        if (!focused)
        {
            var certificate = Assert.Single(nodes, node => node.After?.CallSites.Any(location =>
                location.Path == "src/Servers/Kestrel/samples/WebTransportSampleApp/Program.cs" && location.Line == 17) == true
                && node.Label.Contains("GenerateManualCertificate", StringComparison.Ordinal));
            Assert.Equal("src/Servers/Kestrel/samples/WebTransportSampleApp/Program.cs", certificate.After!.Definition!.Path);
            Assert.Equal(47, certificate.After.Definition.Line);
            return;
        }
        var owner = Assert.Single(result.Trees, node => node.Label == "StreamTracker.GetNextStreamOwner");
        Assert.Equal('+', owner.Mark);
        Assert.Equal("Interlocked.Increment", Assert.Single(owner.Children).Label);
        var add = Assert.Single(result.Trees, node => node.Label == "StreamTracker.AddStream");
        Assert.Contains("long streamOwner", add.After!.Signature);
        var duplicate = Assert.Single(add.Children, node => node.Label == "if (!_lookup.TryAdd(streamId, (streamOwner, newConverter)))");
        Assert.Equal('+', duplicate.Mark);
        Assert.Equal("new HubException", Assert.Single(duplicate.Children).Label);
        var completion = Assert.Single(result.Trees, node => node.After?.SymbolId == "source::Microsoft.AspNetCore.SignalR.StreamTracker.TryComplete(string,long)");
        Assert.Equal('+', completion.Mark);
        Assert.Equal(["_lookup.TryGetValue", "KeyValuePair.Create", "_lookup.TryRemove"], completion.Children.Take(3).Select(node => node.Label));
        var converter = Assert.Single(completion.Children, node => node.After?.Dispatch == "possible");
        Assert.Equal(["source::Microsoft.AspNetCore.SignalR.StreamTracker.ChannelConverter<T>.TryComplete(global::System.Exception)"], converter.After!.TargetIds);
        Assert.Equal("_channel.Writer.TryComplete", Assert.Single(converter.Children).Label);
        var cleanup = Assert.Single(result.Trees, node => node.Label == "DefaultHubDispatcher<THub>.CleanupInvocation");
        Assert.Contains("long? streamOwner", cleanup.After!.Signature);
        var owned = Assert.Single(cleanup.Children, node => node.Mark == '+' && node.Label == "if (streamOwner is not null)");
        Assert.Contains(Descendants(owned.Children), node => node.After?.SymbolId == completion.After!.SymbolId);
        var replacement = Assert.Single(result.Trees, node => node.Label == "DefaultHubDispatcher<THub>.ReplaceArguments");
        Assert.Contains("ref long? streamOwner", replacement.After!.Signature);
        var finalizer = Assert.Single(replacement.Children, node => node.Mark == '+' && node.Label == "finally");
        var failed = Assert.Single(finalizer.Children, node => node.Label == "if (!argumentsReplaced)");
        var cancellation = Assert.Single(failed.Children, node => node.Label == "if (cts is not null)");
        Assert.Equal(".Dispose", Assert.Single(cancellation.Children).Label);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
