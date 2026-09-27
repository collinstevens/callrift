using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OrchardWorkflowExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Trees.Count);
        Assert.All(result.Trees, node => Assert.Equal(node.Before!.SymbolId, node.After!.SymbolId));
        if (restored)
        {
            Assert.Equal(6, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, diagnostic =>
            {
                Assert.Equal("unresolved-call", diagnostic.Code);
                Assert.EndsWith("/ShapePagerTag.cs", diagnostic.Location!.Path);
                Assert.StartsWith("Cannot bind objectValue.", diagnostic.Message);
            });
        }
        else
        {
            Assert.Equal(17172, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(19, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
            Assert.Equal(6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        }
        var create = Assert.Single(result.Trees, node => node.Label == "ActivityController.Create");
        var editorGuard = Assert.Single(create.Children, node => node.Label == "if (!activity.HasEditor)");
        Assert.StartsWith("IActivity.get_HasEditor", create.Children[create.Children.ToList().IndexOf(editorGuard) - 1].Label);
        var post = Assert.Single(editorGuard.Children);
        Assert.Equal("ActivityController.Create", post.Label);
        var edit = Assert.Single(result.Trees, node => node.Label == "WorkflowTypeController.Edit");
        var missing = Assert.Single(edit.Children, node => node.Label == "WorkflowTypeExtensions.IsMissingStartActivity");
        Assert.Equal('+', missing.Mark);
        var success = Assert.Single(edit.Children, node => node.Label.EndsWith("SuccessAsync", StringComparison.Ordinal));
        Assert.Equal(edit.Children.ToList().IndexOf(success) + 1, edit.Children.ToList().IndexOf(missing));
        var warning = Assert.Single(edit.Children, node => node.Label == "if (workflowType.IsMissingStartActivity())");
        Assert.Equal('+', warning.Mark);
        Assert.Contains(warning.Children, node => node.Label.EndsWith("WarningAsync", StringComparison.Ordinal));
        Assert.Equal("WorkflowTypeUpdateModel.get_Id", edit.Children[edit.Children.ToList().IndexOf(warning) + 1].Label);
        if (!focused)
        {
            Assert.Equal("depth-limit", post.Omission!.Reason);
            Assert.Equal("depth-limit", missing.Omission!.Reason);
            return;
        }
        Assert.Equal(["ActivityExtensions.IsEvent", "if (activity.IsEvent())", "ActivityRecord.set_IsStart"], post.Children.Where(node => node.Mark == '+').Select(node => node.Label));
        var eventGuard = Assert.Single(post.Children, node => node.Label == "if (activity.IsEvent())");
        var hasStart = Assert.Single(eventGuard.Children);
        Assert.Equal(restored ? "WorkflowTypeExtensions.HasStartActivity" : "? workflowType.HasStartActivity", hasStart.Label);
        if (restored) Assert.Equal("depth-limit", hasStart.Omission!.Reason);
        var setter = post.Children.ToList().FindIndex(node => node.Label == "ActivityRecord.set_IsStart");
        Assert.Equal(post.Children.ToList().IndexOf(eventGuard) + 1, setter);
        Assert.Equal(restored ? "WorkflowType.get_Activities" : "? workflowType.Activities.Add", post.Children[setter + 1].Label);
        Assert.Equal(["ArgumentNullException.ThrowIfNull", "WorkflowType.get_Activities", "ICollection<T>.get_Count", "if (workflowType.Activities.Count > 0)"], missing.Children.Select(node => node.Label));
        var guardedHelper = Assert.Single(missing.Children[^1].Children);
        Assert.Equal("WorkflowTypeExtensions.HasStartActivity", guardedHelper.Label);
        Assert.Equal("depth-limit", guardedHelper.Omission!.Reason);
        var metadata = Assert.Single(create.Children, node => node.Label == "IShape.get_Metadata");
        Assert.Equal("ShapeMetadata.set_Type", create.Children[create.Children.ToList().IndexOf(metadata) + 1].Label);
        var displayDrivers = Descendants(create.Children).Where(node => node.Label.StartsWith("⇢ DisplayDriver<", StringComparison.Ordinal)).ToArray();
        Assert.Equal(restored ? 34 : 87, displayDrivers.Length);
        Assert.All(displayDrivers, node => Assert.Equal("depth-limit", node.Omission!.Reason));
        if (!restored) return;
        var generatedMetadata = metadata.Children.Where(node => node.After!.Definition!.Path.Contains("/obj/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, generatedMetadata.Length);
        Assert.All(generatedMetadata, node =>
        {
            var lazy = Assert.Single(node.Children);
            Assert.Equal("if (_metadata is null)", lazy.Label);
            var constructor = Assert.Single(lazy.Children);
            Assert.Equal("new ShapeMetadata", constructor.Label);
            Assert.Equal("depth-limit", constructor.Omission!.Reason);
        });
        Assert.Equal(["IHtmlLocalizer.get_Item", "NotifierExtensions.WarningAsync"], warning.Children.Select(node => node.Label));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
