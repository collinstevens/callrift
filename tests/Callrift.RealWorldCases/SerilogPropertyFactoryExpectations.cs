using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class SerilogPropertyFactoryExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal("test-project-inferred", Assert.Single(result.Diagnostics).Code);
        if (!focused)
        {
            Assert.Equal(114, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "Log.CloseAndFlush");
            Assert.Contains(result.Trees, node => node.Label == "LoggerAuditSinkConfiguration.Logger");
            return;
        }

        var root = Assert.Single(result.Trees);
        Assert.Equal("LogEvent.AddPropertyIfAbsent", root.Label);
        Assert.Equal("signature changed", root.Detail);
        Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
        Assert.StartsWith("internal ", root.Before.Signature);
        Assert.StartsWith("public ", root.After.Signature);
        Assert.Equal(["Guard.AgainstNull", "_properties.ContainsKey", "if (!_properties.ContainsKey(name))"],
            root.Children.Select(node => node.Label));
        var guard = root.Children[0];
        Assert.Equal('+', guard.Mark);
        Assert.Null(guard.Before);
        Assert.Equal(219, Assert.Single(guard.After!.CallSites).Line);
        var nullArgument = Assert.Single(guard.Children);
        Assert.Equal("if (argument is null)", nullArgument.Label);
        Assert.Equal("new ArgumentNullException", Assert.Single(nullArgument.Children).Label);
        Assert.Equal(' ', root.Children[1].Mark);
        var absent = root.Children[2];
        Assert.Equal(' ', absent.Mark);
        Assert.Equal(["if (factory is ILogEventPropertyValueFactory factory2)", "else (!(factory is ILogEventPropertyValueFactory factory2))", "_properties.Add"],
            absent.Children.Select(node => node.Label));
        var valueFactory = Assert.Single(absent.Children[0].Children);
        Assert.Equal("ILogEventPropertyValueFactory.CreatePropertyValue", valueFactory.Label);
        Assert.Equal("possible", valueFactory.After!.Dispatch);
        Assert.Equal(3, valueFactory.After.TargetIds.Count);
        var propertyFactory = absent.Children[1];
        Assert.Equal(["ILogEventPropertyFactory.CreateProperty", "LogEventProperty.get_Value"],
            propertyFactory.Children.Select(node => node.Label));
        Assert.Equal("possible", propertyFactory.Children[0].After!.Dispatch);
        Assert.Equal(2, propertyFactory.Children[0].After!.TargetIds.Count);
        Assert.All(absent.Children, node => Assert.Equal(' ', node.Mark));
    }
}
