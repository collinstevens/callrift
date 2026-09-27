using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class PollySecondaryExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var workspace = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        if (!focused)
        {
            Assert.Equal(workspace ? 22 : 7, result.Trees.Count);
            var copy = Assert.Single(result.Trees, node => node.Label == "new TelemetryOptions");
            Assert.Equal('+', copy.Children[0].Mark);
            Assert.EndsWith(".TelemetryOptions..ctor()", copy.Children[0].After!.SymbolId);
            Assert.Equal(["Guard.NotNull", "TelemetryOptions.get_TelemetryListeners", "TelemetryOptions.get_LoggerFactory",
                "TelemetryOptions.set_LoggerFactory", "TelemetryOptions.get_MeteringEnrichers", "TelemetryOptions.get_ResultFormatter",
                "TelemetryOptions.set_ResultFormatter", "TelemetryOptions.get_SeverityProvider", "TelemetryOptions.set_SeverityProvider"],
                copy.Children.Skip(1).Select(node => node.Label));
            if (!workspace)
                Assert.Contains(result.Trees, node => node.Label == "cake.cs::<top-level>");
            return;
        }

        var root = Assert.Single(result.Trees);
        Assert.Equal("TaskExecution<T>.InitializeAsync", root.Label);
        Assert.Equal(["TaskExecution<T>.set_AttemptNumber", "TaskExecution<T>.set_Type"], root.Children.Take(2).Select(node => node.Label));
        var secondary = Assert.Single(root.Children, node => node.Label == "if (type == HedgedTaskType.Secondary)");
        var helper = Assert.Single(secondary.Children, node => node.Label == "TaskExecution<T>.TryCreateSecondaryActionAsync");
        Assert.Equal('+', helper.Mark);
        Assert.Equal(["try", "catch (Exception e)"], helper.Children.Select(node => node.Label));
        var creation = helper.Children[0];
        Assert.Equal(["HedgingHandler<T>.get_ActionGenerator", "TaskExecution<T>.CreateArguments", "_handler.ActionGenerator", "if (action == null)"],
            creation.Children.Select(node => node.Label));
        Assert.Equal("TaskExecution<T>.ResetAsync", creation.Children[3].Children[0].Label);
        Assert.Equal(["TaskExecution<T>.UpdateOutcomeAsync", "TaskExecution<T>.set_ExecutionTaskSafe"], helper.Children[1].Children.TakeLast(2).Select(node => node.Label));
        Assert.Equal(["TaskExecution<T>.ExecuteSecondaryActionAsync", "TaskExecution<T>.set_ExecutionTaskSafe"], secondary.Children.TakeLast(2).Select(node => node.Label));
        var primary = Assert.Single(root.Children, node => node.Label == "else (!(type == HedgedTaskType.Secondary))");
        Assert.Equal(["TaskExecution<T>.ExecutePrimaryActionAsync", "TaskExecution<T>.set_ExecutionTaskSafe"], primary.Children.Select(node => node.Label));
        Assert.DoesNotContain(secondary.Children, node => node.Label.Contains("earlyReturn", StringComparison.Ordinal));
        var context = Assert.Single(root.Children, node => node.Label == "ResilienceContext.InitializeFrom");
        var properties = Assert.Single(context.Children, node => node.Label == "ResilienceProperties.AddOrReplaceProperties");
        var assignment = properties.Children[1].Children[0].Children;
        Assert.Equal(["ResilienceProperties.get_Options", "KeyValuePair<TKey, TValue>.get_Key", "KeyValuePair<TKey, TValue>.get_Value"],
            assignment.Take(3).Select(node => node.Label));
        Assert.StartsWith("IDictionary<TKey, TValue>.set_Item", assignment[3].Label);
    }
}
