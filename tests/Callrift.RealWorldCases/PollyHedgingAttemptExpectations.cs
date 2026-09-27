using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyHedgingAttemptExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Contains("generic-context-limit", result.Coverage.Limitations);
        Assert.Equal(restored ? 14 : 15, result.Diagnostics.Count(diagnostic => diagnostic.Code == "generic-context-limit"));
        if (restored)
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("generic-context-limit", diagnostic.Code));
        else
        {
            Assert.Equal(1935, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "unresolved-static-initializer");
            Assert.Equal(6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        }

        if (!focused)
        {
            Assert.Equal(restored ? 22 : 2, result.Trees.Count);
            var getter = Assert.Single(result.Trees, node => node.Label == "HedgingPredicateArguments<TResult>.get_AttemptNumber");
            Assert.Equal('+', getter.Mark);
            Assert.Null(getter.Before);
            Assert.Equal("public readonly Polly.Hedging.HedgingPredicateArguments<TResult>.get_AttemptNumber() -> int?", getter.After!.Signature);
            Assert.Contains(result.Trees, node => node.Label == (restored ? "ResiliencePipeline.ExecuteAsync" : "TaskExecution<T>.InitializeAsync"));
            return;
        }

        Assert.Equal(3, result.Trees.Count);
        var update = result.Trees[0];
        Assert.Equal("TaskExecution<T>.UpdateOutcomeAsync", update.Label);
        Assert.Equal(update.Before!.SymbolId, update.After!.SymbolId);
        Assert.Equal(update.Before.Signature, update.After.Signature);
        Assert.Equal("TaskExecution<T>.get_Context", update.Children[0].Label);
        var attempt = update.Children[1];
        Assert.Equal("TaskExecution<T>.get_AttemptNumber", attempt.Label);
        Assert.Equal('+', attempt.Mark);
        Assert.Null(attempt.Before);
        Assert.Equal(242, Assert.Single(attempt.After!.CallSites).Line);
        var constructor = update.Children[2];
        Assert.Equal("new HedgingPredicateArguments<TResult>", constructor.Label);
        Assert.Equal('~', constructor.Mark);
        Assert.Equal("signature changed", constructor.Detail);
        Assert.NotEqual(constructor.Before!.SymbolId, constructor.After!.SymbolId);
        Assert.DoesNotContain("int attemptNumber", constructor.Before.Signature);
        Assert.Contains("int attemptNumber", constructor.After.Signature);
        Assert.Equal(242, Assert.Single(constructor.After.CallSites).Line);
        var forwarding = Assert.Single(constructor.Children);
        Assert.Equal('+', forwarding.Mark);
        Assert.Equal(constructor.Before.SymbolId, forwarding.After!.SymbolId);
        Assert.Equal(32, Assert.Single(forwarding.After.CallSites).Line);
        Assert.Empty(forwarding.Children);
        Assert.Contains(update.Children, node => node.Label == "TaskExecution<T>.get_AttemptNumber" && node.Mark == ' '
            && Assert.Single(node.After!.CallSites).Line == 245);
        Assert.Contains(update.Children, node => node.Label == "_handler.ShouldHandle" && node.Mark == ' ');
        Assert.Contains(update.Children, node => node.Label == "TelemetryUtil.ReportExecutionAttempt" && node.Mark == ' ');

        var existing = result.Trees[1];
        Assert.Equal(constructor.Before.SymbolId, existing.Before!.SymbolId);
        Assert.Equal(existing.Before.SymbolId, existing.After!.SymbolId);
        Assert.Equal(existing.Before.Signature, existing.After.Signature);
        Assert.Equal('~', existing.Mark);
        Assert.Equal("body changed; visible calls unchanged", existing.Detail);
        Assert.Empty(existing.Children);
        var added = result.Trees[2];
        Assert.Equal('+', added.Mark);
        Assert.Null(added.Before);
        Assert.Equal(constructor.After.SymbolId, added.After!.SymbolId);
        Assert.Equal(existing.After.SymbolId, Assert.Single(added.Children).After!.SymbolId);
    }
}
