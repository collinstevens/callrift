using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AutofacDisposalExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.True(result.Truncated);
        if (workspace) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(173, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        }
        if (!focused)
        {
            Assert.Equal(workspace ? 4 : 28, result.Trees.Count);
            Assert.Equal(workspace ? 0 : 24, result.Trees.Count(node => node.After!.Definition!.Path.StartsWith("bench/", StringComparison.Ordinal)));
            Assert.Equal(4, result.Trees.Count(node => node.Label == "ModuleRegistrationExtensions.RegisterAssemblyModules"));
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("ContainerBuilder.Build", root.Label);
        Assert.Equal(root.Before!.Signature, root.After!.Signature);
        Assert.Contains("ContainerBuildOptions.None", root.After.Signature);
        var labels = root.Children.Select(node => node.Label).ToList();
        var creation = Assert.Single(root.Children, node => node.Label == "new Container");
        Assert.Equal(' ', creation.Mark);
        Assert.True(labels.IndexOf("new Container") < labels.IndexOf("try"));
        Assert.True(labels.IndexOf("try") < labels.IndexOf("catch"));
        Assert.True(labels.IndexOf("catch") < labels.IndexOf("ReflectionCacheSet.OnContainerBuildClearCaches"));
        var startup = Assert.Single(root.Children, node => node.Label == "try");
        Assert.Equal('+', startup.Mark);
        Assert.Equal(2, startup.Children.Count);
        var guard = startup.Children[0];
        Assert.Equal("if ((options & ContainerBuildOptions.IgnoreStartableComponents) == ContainerBuildOptions.None)", guard.Label);
        var startables = Assert.Single(guard.Children);
        Assert.Equal("StartableManager.StartStartableComponents", startables.Label);
        var previousGuard = Assert.Single(root.Children, node => node.Label == guard.Label && node.Mark == '-');
        Assert.Equal(Assert.Single(previousGuard.Children).Before!.SymbolId, startables.After!.SymbolId);
        var callbacks = startup.Children[1];
        Assert.Equal("BuildCallbackManager.RunBuildCallbacks", callbacks.Label);
        var previousCallbacks = Assert.Single(root.Children, node => node.Label == callbacks.Label && node.Mark == '-');
        Assert.Equal(previousCallbacks.Before!.SymbolId, callbacks.After!.SymbolId);
        var failure = Assert.Single(root.Children, node => node.Label == "catch");
        Assert.Equal('+', failure.Mark);
        var dispose = Assert.Single(failure.Children);
        Assert.EndsWith("::Autofac.Util.Disposable.Dispose()", dispose.After!.SymbolId);
        Assert.Equal(190, Assert.Single(dispose.After.CallSites).Line);
        Assert.Equal(["Interlocked.Exchange", "Disposable.Dispose → Container.Dispose", "GC.SuppressFinalize"],
            dispose.Children.Select(node => node.Label));
        var dispatch = dispose.Children[1];
        Assert.Equal("possible", dispatch.After!.Dispatch);
        Assert.EndsWith("::Autofac.Util.Disposable.Dispose(bool)", dispatch.After.SymbolId);
        Assert.EndsWith("::Autofac.Core.Container.Dispose(bool)", Assert.Single(dispatch.After.TargetIds));
        Assert.Equal("depth limit", dispatch.Detail);
        Assert.DoesNotContain(Descendants(dispose.Children), node => node.Label == "if (wasDisposed == DisposedFlag)");
        Assert.DoesNotContain(Descendants(failure.Children), node => node.Label.Contains("throw", StringComparison.Ordinal));
        Assert.Equal(' ', Assert.Single(root.Children, node => node.Label == "ReflectionCacheSet.OnContainerBuildClearCaches").Mark);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
