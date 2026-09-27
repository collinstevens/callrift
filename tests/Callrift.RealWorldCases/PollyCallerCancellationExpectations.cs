using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyCallerCancellationExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        if (!focused)
        {
            Assert.Equal(workspace ? 21 : 13, result.Trees.Count);
            Assert.DoesNotContain(result.Trees, node => node.Label == "OutcomeUtilities.WithCallerCancellationToken");
            return;
        }

        var root = Assert.Single(result.Trees);
        Assert.Equal("OutcomeUtilities.WithCallerCancellationToken", root.Label);
        Assert.Equal('+', root.Mark);
        Assert.Equal("CancellationToken.get_IsCancellationRequested", root.Children[0].Label);
        Assert.Equal("if (callerToken.IsCancellationRequested)", root.Children[1].Label);
        Assert.Equal("Outcome<TResult>.get_Exception", Assert.Single(root.Children[1].Children).Label);
        Assert.Equal("if (callerToken.IsCancellationRequested && outcome.Exception is OperationCanceledException oce)", root.Children[2].Label);
        Assert.Equal(["OperationCanceledException.get_CancellationToken", "CancellationToken.op_Inequality"], root.Children[2].Children.Select(node => node.Label));
        var replacement = root.Children[3];
        Assert.Equal("if (callerToken.IsCancellationRequested && outcome.Exception is OperationCanceledException oce &&…", replacement.Label);
        Assert.Equal(25, Assert.Single(replacement.After!.CallSites).Line);
        Assert.Equal(["Exception.get_Message", "new OperationCanceledException", "ExceptionUtilities.TrySetStackTrace", "Outcome.FromException"],
            replacement.Children.Select(node => node.Label));
        var construction = replacement.Children[1];
        Assert.EndsWith(".OperationCanceledException..ctor(string,global::System.Exception,global::System.Threading.CancellationToken)", construction.After!.SymbolId);
        Assert.Equal(27, Assert.Single(construction.After.CallSites).Line);
        var stackTrace = replacement.Children[2];
        Assert.Equal(["Exception.get_StackTrace", "string.IsNullOrWhiteSpace"], stackTrace.Children.Take(2).Select(node => node.Label));
        Assert.Equal(workspace ? "System.Runtime.ExceptionServices.ExceptionDispatchInfo.SetCurrentStackTrace" : "ExceptionUtilities.SetStackTrace",
            stackTrace.Children[^1].Label);
        Assert.All(Descendants(root.Children), node => Assert.Equal('+', node.Mark));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
