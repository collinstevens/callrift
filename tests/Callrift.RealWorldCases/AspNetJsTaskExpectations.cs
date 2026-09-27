using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AspNetJsTaskExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Equal(22846, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(179, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
        if (!focused)
        {
            Assert.Equal(10, result.Trees.Count);
            var added = result.Trees.Where(node => node.Mark == '+').ToArray();
            Assert.Equal(6, added.Length);
            Assert.Equal(2, added.Count(node => node.Label == "new AsyncInteropResult"));
            foreach (var property in new[] { "Source", "Value" })
            {
                var getter = Assert.Single(added, node => node.Label == "AsyncInteropResult.get_" + property);
                var setter = Assert.Single(added, node => node.Label == "AsyncInteropResult.set_" + property);
                Assert.StartsWith("public readonly ", getter.After!.Signature);
                Assert.StartsWith("public init ", setter.After!.Signature);
                Assert.Null(getter.Before);
                Assert.Null(setter.Before);
            }
            return;
        }

        Assert.Equal(3, result.Trees.Count);
        var completion = Assert.Single(result.Trees, node => node.Label == "DotNetDispatcher.EndInvokeDotNetAfterTask");
        Assert.Equal("signature changed", completion.Detail);
        Assert.NotEqual(completion.Before!.SymbolId, completion.After!.SymbolId);
        var cancellation = Assert.Single(completion.Children, node => node.Label == "if (task.IsCanceled)");
        Assert.Equal('+', cancellation.Mark);
        Assert.Equal(["new TaskCanceledException", "new DotNetInvocationResult", "JSRuntime.EndInvokeDotNet"],
            cancellation.Children.Select(node => node.Label));
        var canceled = Assert.Single(completion.Children, node => node.Label == "Task.get_IsCanceled");
        Assert.Equal('+', canceled.Mark);
        Assert.True(completion.Children.ToList().IndexOf(canceled) < completion.Children.ToList().IndexOf(cancellation));
        var genericResult = Assert.Single(completion.Children, node => node.Label == "else (!(isNonGenericTask))");
        Assert.Equal("TaskGenericsUtil.GetTaskResult", Assert.Single(genericResult.Children).Label);
        var unconditionalResult = Assert.Single(completion.Children, node => node.Label == "TaskGenericsUtil.GetTaskResult");
        Assert.Equal('-', unconditionalResult.Mark);
        var options = Assert.Single(completion.Children, node => node.Label == "JSRuntime.get_JsonSerializerOptions");
        Assert.Equal(' ', options.Mark);
        Assert.Equal(options.Before!.SymbolId, options.After!.SymbolId);
    }
}
