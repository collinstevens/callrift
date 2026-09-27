using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogDottedPropertyExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        if (!focused)
        {
            Assert.Equal(113, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "Log.BindMessageTemplate");
            var formatter = Assert.Single(result.Trees, node => node.Label == "new MessageTemplateTextFormatter");
            Assert.Contains(Descendants(formatter.Children), node => node.Label == "initialization of MessageTemplateParser" && node.Mark == '-');
            Assert.Contains(Descendants(result.Trees), node => node.Label == "⇢ Logger.Write" && node.Detail == "changes below depth limit");
            return;
        }

        Assert.Equal(["new MessageTemplateParser", "MessageTemplateParser.ParsePropertyToken"], result.Trees.Select(node => node.Label));
        var constructor = result.Trees[0];
        Assert.Equal(constructor.Before!.SymbolId, constructor.After!.SymbolId);
        var initializer = Assert.Single(constructor.Children, node => node.Label == "possible initialization of MessageTemplateParser");
        Assert.Equal('-', initializer.Mark);
        Assert.Contains(Descendants(initializer.Children), node => node.Label == "AppContext.TryGetSwitch" && node.Mark == '-');
        Assert.Equal("new Object", Assert.Single(constructor.Children, node => node.After is not null).Label);

        var parser = result.Trees[1];
        Assert.Equal(parser.Before!.Signature, parser.After!.Signature);
        var digitBranch = Assert.Single(parser.Children, node => node.Label == "if (char.IsDigit(propertyName[0]))");
        var digitGuard = Assert.Single(Descendants(digitBranch.Children), node => node.Label == "if (!char.IsDigit(c))");
        Assert.Equal(115, Assert.Single(Assert.Single(digitGuard.Children).After!.CallSites).Line);
        var nameBranch = Assert.Single(parser.Children, node => node.Label == "else (!(char.IsDigit(propertyName[0])))");
        var continuation = Assert.Single(Descendants(nameBranch.Children), node => node.Label == "MessageTemplateParser.TryContinuePropertyName");
        Assert.Equal('+', continuation.Mark);
        Assert.Equal("private static Serilog.Parsing.MessageTemplateParser.TryContinuePropertyName(char c, ref bool beginIdent) -> bool", continuation.After!.Signature);
        Assert.Equal(124, Assert.Single(continuation.After.CallSites).Line);
        Assert.Contains(Descendants(continuation.Children), node => node.Label == "char.IsLetter");
        Assert.Contains(continuation.Children, node => node.Label == "char.IsLetterOrDigit");
        Assert.DoesNotContain(Descendants(continuation.Children), node => node.Label == "if (c is '.')");
        var trailingDot = Assert.Single(nameBranch.Children, node => node.Label == "if (beginIdent)");
        Assert.Equal(130, Assert.Single(Assert.Single(trailingDot.Children).After!.CallSites).Line);
        Assert.Contains(Descendants(parser.Children), node => node.Label == "MessageTemplateParser.IsValidInPropertyName" && node.Mark == '-');
        Assert.Equal(6, Descendants(parser.Children).Count(node => node.Label == "Index.op_Implicit" && node.Mark == '+'));
        Assert.Contains(parser.Children, node => node.Label == "new PropertyToken" && node.Mark == ' ');
        Assert.Contains(parser.Children, node => node.Label == "if (format != null)" && node.Mark == ' ');
        Assert.Contains(parser.Children, node => node.Label == "if (alignment != null)" && node.Mark == ' ');
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
