using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyNullPropertyExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(workspace ? 14 : 15, result.Diagnostics.Count(diagnostic => diagnostic.Code == "generic-context-limit"));
        Assert.Equal(workspace ? 0 : 1847, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(workspace ? 0 : 2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
        Assert.Equal(workspace ? 0 : 1, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        Assert.Equal(workspace ? 0 : 6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        Assert.Equal(focused ? 2 : 1, result.Trees.Count);
        var caller = result.Trees[0];
        Assert.Equal("ResilienceProperties.GetValue", caller.Label);
        Assert.Equal(' ', caller.Mark);
        Assert.Equal(40, caller.Before!.Definition!.Line);
        Assert.Equal(54, caller.After!.Definition!.Line);
        var changed = Assert.Single(caller.Children);
        VerifyBodyChange(changed);
        Assert.Equal(42, Assert.Single(changed.Before!.CallSites).Line);
        Assert.Equal(56, Assert.Single(changed.After!.CallSites).Line);
        Assert.Equal("ResilienceProperties.get_Options", changed.Children[0].Label);
        Assert.Equal("ResiliencePropertyKey<TValue>.get_Key", changed.Children[1].Label);
        Assert.All(changed.Children.Take(2), node =>
        {
            Assert.Equal(' ', node.Mark);
            Assert.Equal("resolved", node.After!.Binding);
            Assert.Equal("direct", node.After.Dispatch);
            Assert.Equal(23, Assert.Single(node.After.CallSites).Line);
        });
        Assert.DoesNotContain(changed.Children, node => node.Kind == "branch");
        if (!focused && workspace)
        {
            Assert.Equal(2, changed.Children.Count);
            return;
        }

        var dictionary = changed.Children[2];
        Assert.Equal(' ', dictionary.Mark);
        Assert.Equal("resolved", dictionary.After!.Binding);
        Assert.Equal("metadata", dictionary.After.Origin);
        Assert.Equal(workspace ? "direct" : "possible", dictionary.After.Dispatch);
        Assert.Equal(workspace ? "Options.TryGetValue" : "Options.TryGetValue → Context.TryGetValue", dictionary.Label);
        Assert.Equal(23, Assert.Single(dictionary.After.CallSites).Line);
        if (workspace)
        {
            Assert.StartsWith("metadata:System.Runtime::System.Collections.Generic.IDictionary", dictionary.After.SymbolId);
            Assert.Empty(dictionary.Children);
        }
        else
        {
            Assert.Equal("source::Polly.Context.TryGetValue(string,out object)", Assert.Single(dictionary.After.TargetIds));
            if (focused)
            {
                Assert.Equal(["Context.get_WrappedDictionary", "WrappedDictionary.TryGetValue"], dictionary.Children.Select(node => node.Label));
                Assert.All(dictionary.Children, node => Assert.Equal(73, Assert.Single(node.After!.CallSites).Line));
            }
            else
            {
                Assert.Equal("depth-limit", dictionary.Omission!.Reason);
                Assert.Empty(dictionary.Children);
            }
        }

        if (focused)
        {
            VerifyBodyChange(result.Trees[1]);
            Assert.Empty(result.Trees[1].After!.CallSites);
            Assert.DoesNotContain(result.Trees[1].Children, node => node.Kind == "branch");
        }
    }

    private static void VerifyBodyChange(DiffNode node)
    {
        Assert.Equal("ResilienceProperties.TryGetValue", node.Label);
        Assert.Equal('~', node.Mark);
        Assert.Equal("body changed; visible calls unchanged", node.Detail);
        Assert.Equal(node.Before!.SymbolId, node.After!.SymbolId);
        Assert.Equal(node.Before.Signature, node.After.Signature);
        Assert.Equal("public Polly.ResilienceProperties.TryGetValue<TValue>(Polly.ResiliencePropertyKey<TValue> key, out TValue value) -> bool", node.After.Signature);
        Assert.Equal(21, node.Before.Definition!.Line);
        Assert.Equal(21, node.After.Definition!.Line);
    }
}
