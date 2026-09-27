using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogAccessorExpectations
{
    public static void VerifyExceptionFormatting(DiffResult result)
    {
        Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        var formatter = Assert.Single(result.Trees);
        Assert.Equal("MessageTemplateTextFormatter.Format", formatter.Label);
        Assert.Null(formatter.Detail);
        Assert.Contains(formatter.Children, node => node.Label == "MessageTemplate.get_TokenArray");
        var guarded = Assert.Single(Descendants(formatter.Children), node => node.Label == "else (!(logEvent.Exception == null))");
        Assert.Equal('-', Assert.Single(guarded.Children, node => node.Label == "LogEvent.get_Exception").Mark);
        var attempt = Assert.Single(guarded.Children, node => node.Label == "try");
        var fallback = Assert.Single(guarded.Children, node => node.Label == "catch (Exception ex)");
        Assert.Equal('+', attempt.Mark);
        Assert.Equal('+', fallback.Mark);
        foreach (var (branch, line) in new[] { (attempt, 97), (fallback, 101) })
        {
            var getter = Assert.Single(branch.Children);
            Assert.Equal("LogEvent.get_Exception", getter.Label);
            Assert.Equal('+', getter.Mark);
            Assert.Equal(line, Assert.Single(getter.After!.CallSites).Line);
            Assert.EndsWith(" -> System.Exception?", getter.After.Signature);
        }
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
