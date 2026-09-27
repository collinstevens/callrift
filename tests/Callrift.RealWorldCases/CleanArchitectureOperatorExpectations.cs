using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class CleanArchitectureOperatorExpectations
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
            Assert.Equal(383, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        }
        Assert.Equal(["ValueObject.op_Equality", "ValueObject.op_Inequality"], result.Trees.Select(node => node.Label));
        Assert.Equal([46, 51], result.Trees.Select(node => node.After!.Definition!.Line));
        var all = Descendants(result.Trees).ToArray();
        Assert.All(all, node =>
        {
            Assert.Equal('+', node.Mark);
            Assert.Null(node.Before);
        });
        Assert.All(result.Trees, node => Assert.Equal("src/Domain/Common/ValueObject.cs", node.After!.Definition!.Path));
        Assert.Equal("ValueObject.EqualOperator", Assert.Single(result.Trees[0].Children).Label);
        Assert.Equal("ValueObject.NotEqualOperator", Assert.Single(result.Trees[1].Children).Label);
        if (!focused) return;
        Assert.DoesNotContain(all, node => node.Label.Contains("left is null ^ right is null", StringComparison.Ordinal));
        Assert.Equal(2, all.Count(node => node.Label is "ValueObject.op_Equality" or "ValueObject.op_Inequality"));
        foreach (var root in result.Trees)
        {
            var helper = Assert.Single(Descendants(root.Children), node => node.Label == "ValueObject.EqualOperator");
            var condition = Assert.Single(helper.Children);
            Assert.Equal("if (left is not null)", condition.Label);
            var equals = Assert.Single(condition.Children);
            Assert.Equal("ValueObject.Equals", equals.Label);
            Assert.Equal("if (!(obj == null))", equals.Children[0].Label);
            Assert.Equal(["obj.GetType", "GetType", "Type.op_Inequality"], equals.Children[0].Children.Select(node => node.Label));
            var typeOperator = equals.Children[0].Children[2];
            Assert.EndsWith("::System.Type.op_Inequality(global::System.Type,global::System.Type)", typeOperator.After!.SymbolId);
            var componentCalls = equals.Children.Where(node => node.After?.Dispatch == "possible").ToArray();
            Assert.Equal(2, componentCalls.Length);
            Assert.All(componentCalls, node =>
            {
                Assert.EndsWith("::CleanArchitecture.Domain.Common.ValueObject.GetEqualityComponents()", node.After!.SymbolId);
                Assert.EndsWith("::CleanArchitecture.Domain.ValueObjects.Colour.GetEqualityComponents()", Assert.Single(node.After.TargetIds));
                if (root.Label == "ValueObject.op_Equality")
                {
                    Assert.Null(node.Omission);
                    var getter = Assert.Single(node.Children);
                    Assert.Equal("Colour.get_Code", getter.Label);
                    Assert.Equal("public CleanArchitecture.Domain.ValueObjects.Colour.get_Code() -> string", getter.After!.Signature);
                    Assert.Equal("src/Domain/ValueObjects/Colour.cs", getter.After.Definition!.Path);
                    Assert.Equal(33, getter.After.Definition.Line);
                }
                else
                {
                    Assert.Empty(node.Children);
                    Assert.Equal("depth-limit", node.Omission!.Reason);
                }
            });
            Assert.Equal("GetEqualityComponents().SequenceEqual", equals.Children[^1].Label);
        }
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
