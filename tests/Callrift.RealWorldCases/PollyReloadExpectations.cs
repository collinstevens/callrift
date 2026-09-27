using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyReloadExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("partial", result.Coverage.Status);
        var root = Assert.Single(result.Trees);
        Assert.Equal("AddResiliencePipelineContext<TKey>.EnableReloads", root.Label);
        var receiver = root.Children[0];
        Assert.Equal("AddResiliencePipelineContext<TKey>.get_RegistryContext", receiver.Label);
        Assert.Equal('-', receiver.Mark);
        Assert.Equal("AddResiliencePipelineContext<TKey>.get_ServiceProvider", root.Children[1].Label);
        Assert.Equal(' ', root.Children[1].Mark);
        var helper = root.Children[^1];
        Assert.Equal("AddResiliencePipelineContext<TKey>.EnableReloadsWithMonitor", helper.Label);
        Assert.Equal('+', helper.Mark);
        if (!focused) return;

        Assert.Equal(["Guard.NotNull", "AddResiliencePipelineContext<TKey>.get_RegistryContext",
            "ConfigureBuilderContextExtensions.EnableReloads"], helper.Children.Select(node => node.Label));
        Assert.Equal(receiver.Before!.SymbolId, helper.Children[1].After!.SymbolId);
        var previous = Assert.Single(root.Children, node => node.Label == "ConfigureBuilderContextExtensions.EnableReloads");
        var relocated = helper.Children[2];
        Assert.Equal(previous.Before!.SymbolId, relocated.After!.SymbolId);
        Assert.Equal(previous.Children.Select(node => node.Label), relocated.Children.Select(node => node.Label));
        var token = Assert.Single(relocated.Children, node => node.Label == "CancellationTokenSource.get_Token");
        Assert.Equal(50, Assert.Single(token.After!.CallSites).Line);
        var registration = Assert.Single(relocated.Children, node => node.Label == "ConfigureBuilderContext<TKey>.AddReloadToken");
        Assert.Equal(["CancellationToken.get_CanBeCanceled", "if (!(!cancellationToken.CanBeCanceled))",
            "ConfigureBuilderContext<TKey>.get_ReloadTokens", "ReloadTokens.Add"], registration.Children.Select(node => node.Label));
        Assert.Equal("CancellationToken.get_IsCancellationRequested", Assert.Single(registration.Children[1].Children).Label);
        var disposal = Assert.Single(relocated.Children, node => node.Label == "ConfigureBuilderContext<TKey>.OnPipelineDisposed");
        Assert.Equal(["ConfigureBuilderContext<TKey>.get_DisposeCallbacks", "DisposeCallbacks.Add",
            "if (registration is not null)", "source.Dispose"], disposal.Children.Skip(1).Select(node => node.Label));
        var possibleDisposal = Assert.Single(disposal.Children[3].Children);
        Assert.Equal(result.Coverage.Mode == "msbuild" ? 5 : 12, possibleDisposal.Children.Count);
    }
}
