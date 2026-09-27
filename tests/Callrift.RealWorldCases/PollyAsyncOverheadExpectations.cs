using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollyAsyncOverheadExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(workspace ? 22 : 23, result.Diagnostics.Count(diagnostic => diagnostic.Code == "generic-context-limit"));
        Assert.Equal(workspace ? 0 : 2017, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(workspace ? 0 : 2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
        Assert.Equal(workspace ? 0 : 1, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        Assert.Equal(workspace ? 0 : 6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
        if (!focused)
        {
            Assert.Equal(workspace ? 47 : 156, result.Trees.Count);
            var clone = Assert.Single(result.Trees, node => node.Label == "clone RegistryPipelineComponentBuilder<TBuilder, TKey>.Builder");
            Assert.Equal('-', clone.Mark);
            Assert.Equal("new RegistryPipelineComponentBuilder<TBuilder, TKey>.Builder", Assert.Single(clone.Children).Label);
            Assert.Null(clone.After);
            Assert.Contains(result.Trees, node => node.Label == "ResiliencePipelineRegistry<TKey>.GetOrAddPipeline");
            if (workspace)
            {
                Assert.Contains(result.Trees, node => node.Label == "CircuitBreakerManualControl.CloseAsync");
                Assert.DoesNotContain(result.Trees, node => node.Label.StartsWith("Bulkhead.", StringComparison.Ordinal));
                Assert.DoesNotContain(result.Trees, node => node.Label.Contains("DisposeWrapper", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains(result.Trees, node => node.Label == "Bulkhead.Bulkhead_Synchronous");
                Assert.Contains(result.Trees, node => node.Label == "new DisposeWrapper" && node.Mark == '-');
                Assert.Contains(result.Trees, node => node.Label == "TimeoutResilienceStrategy.CreateRegistration"
                    && node.Detail == "body changed; visible calls unchanged");
            }
            return;
        }

        Assert.Equal(["CircuitBreakerManualControl.Initialize", "CircuitStateController<T>.OnActionPreExecuteAsync",
            "PipelineComponent.ExecuteCoreSync", "ReloadableComponent.Reload"], result.Trees.Select(node => node.Label));
        VerifyInitialization(result.Trees[0]);
        VerifyHalfOpen(result.Trees[1]);
        VerifySynchronousCallback(result.Trees[2], workspace);
        VerifyReload(result.Trees[3], workspace);
    }

    private static void VerifyInitialization(DiffNode root)
    {
        Assert.Equal(["_onIsolate.Add", "_onReset.Add", "if (_isolated)", "if (isolated)",
            "new CircuitBreakerManualControl.RegistrationDisposable"], root.Children.Select(node => node.Label));
        var previous = root.Children[2];
        var current = root.Children[3];
        Assert.Equal('-', previous.Mark);
        Assert.Equal('+', current.Mark);
        Assert.Equal("CircuitBreakerManualControl.IsolateAsync", previous.Children[3].Label);
        Assert.Equal("onIsolate", current.Children[3].Label);
        Assert.Equal(45, Assert.Single(current.Children[3].After!.CallSites).Line);
        Assert.DoesNotContain(current.Children, node => node.Label == "CircuitBreakerManualControl.IsolateAsync");
        var registration = root.Children[4];
        Assert.Equal("signature changed", registration.Detail);
        Assert.Contains("System.Action disposeAction", registration.Before!.Signature);
        Assert.Contains("CircuitBreakerManualControl owner", registration.After!.Signature);
        Assert.Equal(["_onIsolate.Remove", "_onReset.Remove"], registration.Children.Where(node => node.Mark == '-').Select(node => node.Label));
        Assert.All(registration.Children.Where(node => node.Mark == '-'), node => Assert.Equal("callback", node.Before!.Relation));
    }

    private static void VerifyHalfOpen(DiffNode root)
    {
        Assert.Equal("signature changed", root.Detail);
        Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
        Assert.StartsWith("public async ", root.Before.Signature);
        Assert.StartsWith("public Polly.", root.After.Signature);
        var children = root.Children.ToList();
        var addedFailure = Assert.Single(children, node => node.Label == "if (exception is not null)" && node.Mark == '+');
        var removedFailure = Assert.Single(children, node => node.Label == "if (exception is not null)" && node.Mark == '-');
        var scheduling = Assert.Single(children, node => node.Label == "CircuitStateController<T>.ExecuteScheduledTaskAsync");
        Assert.True(children.IndexOf(addedFailure) < children.IndexOf(scheduling));
        Assert.True(children.IndexOf(removedFailure) > children.IndexOf(scheduling));
        Assert.Contains(removedFailure.Children, node => node.Label == "Outcome.FromException");
        Assert.Contains(addedFailure.Children, node => node.Label == "new Outcome<TResult>");
        var incomplete = Assert.Single(children, node => node.Label == "if (!task.IsCompleted)");
        Assert.Equal('+', incomplete.Mark);
        Assert.Equal(["ResilienceContext.get_ContinueOnCapturedContext", "CircuitStateController<T>.WaitHalfOpenTask"],
            incomplete.Children.Select(node => node.Label));
        Assert.Equal(["task.GetAwaiter", "task.GetAwaiter().GetResult"], children.TakeLast(2).Select(node => node.Label));
    }

    private static void VerifySynchronousCallback(DiffNode root, bool workspace)
    {
        Assert.Equal("signature changed", root.Detail);
        Assert.EndsWith("-> Polly.Outcome<TResult>", root.Before!.Signature);
        Assert.EndsWith("-> TResult", root.After!.Signature);
        var invocation = Assert.Single(root.Children, node => node.After is not null
            && node.Label == (workspace ? "PipelineComponent.ExecuteCore" : "? ExecuteCore"));
        Assert.Equal(workspace ? "resolved" : "unresolved", invocation.After!.Binding);
        var capture = Assert.Single(invocation.Children, node => node.Label == "catch (Exception e)");
        Assert.Equal('+', capture.Mark);
        Assert.Contains(capture.Children, node => node.Label == "new Outcome<TResult>");
        Assert.Equal(["TaskHelper.GetResult", "Outcome<TResult>.GetResultOrRethrow"], root.Children.TakeLast(2).Select(node => node.Label));
        Assert.Equal('+', root.Children[^1].Mark);
    }

    private static void VerifyReload(DiffNode root, bool workspace)
    {
        Assert.Equal('+', root.Mark);
        Assert.Null(root.Before);
        var children = root.Children.ToList();
        var returned = Assert.Single(children, node => node.Label == "ResilienceContextPool.Return → ResilienceContextPool.SharedPool.Return");
        var replacement = Assert.Single(children, node => node.Label == "try");
        Assert.True(children.IndexOf(returned) < children.IndexOf(replacement));
        Assert.Equal(["_factory", "ReloadableComponent.set_Component"], replacement.Children.Select(node => node.Label));
        var setter = replacement.Children[1].After!;
        Assert.Equal("resolved", setter.Binding);
        Assert.Equal("direct", setter.Dispatch);
        Assert.Equal(setter.SymbolId, Assert.Single(setter.TargetIds));
        Assert.Equal(29, setter.Definition!.Line);
        var assignment = Assert.Single(setter.CallSites);
        Assert.Equal(70, assignment.Line);
        Assert.Equal(14, assignment.Column);
        var registration = Assert.Single(children, node => node.Label == "ReloadableComponent.TryRegisterOnReload");
        Assert.DoesNotContain(registration.Children, node => node.Label == "if (reloadTokens.Count == 0)");
        var callback = registration.Children[^1];
        Assert.Equal(workspace ? "_tokenSource.Token.UnsafeRegister" : "_tokenSource.Token.Register", callback.Label);
        var cycle = Assert.Single(callback.Children);
        Assert.Equal(root.After!.SymbolId, cycle.After!.SymbolId);
        Assert.Equal("↺ cycle", cycle.Detail);
        Assert.Equal("ReloadableComponent.DisposeDiscardedComponentSafeAsync", children[^1].Label);
    }
}
