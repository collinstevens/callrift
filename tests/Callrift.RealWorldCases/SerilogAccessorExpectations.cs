using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogAccessorExpectations
{
    public static void VerifyExtraArguments(DiffResult result)
    {
        var binder = Assert.Single(result.Trees);
        Assert.Equal("MessageTemplate.get_NamedProperties", binder.Children[0].Label);
        var missing = Assert.Single(binder.Children, node => node.Label == "if (namedProperties == null)");
        var surplus = Assert.Single(missing.Children, node => node.Label == "if (messageTemplateParameters.Length > 0)");
        Assert.Equal('+', surplus.Mark);
        Assert.Equal("SelfLog.WriteLine", Assert.Single(surplus.Children).Label);
    }

    public static void VerifyNullKey(DiffResult result)
    {
        var formatter = Assert.Single(result.Trees);
        Assert.Equal("DictionaryValue.get_Elements", formatter.Children[0].Label);
        var loop = Assert.Single(formatter.Children, node => node.Label == "foreach (var element in dictionary.Elements)");
        Assert.Equal("ScalarValue.get_Value", loop.Children[0].Label);
        var nonNull = Assert.Single(loop.Children, node => node.Label == "else (!(key is null))");
        Assert.Equal('+', nonNull.Mark);
        Assert.Equal(["key.ToString", "JsonValueFormatter.WriteQuotedJsonString"], nonNull.Children.Select(node => node.Label));
        Assert.Equal("possible", nonNull.Children[0].After!.Dispatch);
        Assert.Equal(4, nonNull.Children[0].After!.TargetIds.Count);
        Assert.Equal(7, nonNull.Children[0].Children.Count);
        Assert.Equal('-', Assert.Single(loop.Children, node => node.Label == "JsonValueFormatter.WriteQuotedJsonString").Mark);
        var character = Assert.Single(Descendants(formatter.Children), node => node.Label == "if (value is char)");
        Assert.Equal("JsonValueFormatter.FormatStringValue", Assert.Single(character.Children).Label);
    }

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
