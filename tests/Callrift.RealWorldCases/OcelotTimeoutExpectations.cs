using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotTimeoutExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Trees.Count);
        if (result.Coverage.Mode == "msbuild") Assert.Empty(result.Diagnostics);
        else Assert.Equal(1123, result.Diagnostics.Count);
        if (!focused)
        {
            Assert.Contains(result.Trees, node => node.Label == "HttpRequesterMiddleware.Invoke");
            Assert.Contains(result.Trees, node => node.Label == (result.Coverage.Mode == "msbuild"
                ? "ResponderMiddleware.Invoke" : "samples/Metadata/Program.cs::<top-level>"));
            return;
        }
        var mapper = Assert.Single(result.Trees, node => node.Label == "HttpExceptionToErrorMapper.Map");
        Assert.Equal(mapper.Before!.SymbolId, mapper.After!.SymbolId);
        Assert.Equal(mapper.Before.Signature, mapper.After.Signature);
        var typeGuard = Assert.Single(mapper.Children, node => node.Label == "if (type == typeof(TimeoutException))");
        var getter = Assert.Single(typeGuard.Children, node => node.Label == "Exception.get_InnerException");
        Assert.Equal('+', getter.Mark);
        Assert.Null(getter.Before);
        Assert.Equal(31, Assert.Single(getter.After!.CallSites).Line);
        Assert.EndsWith("::System.Exception.get_InnerException()", getter.After.SymbolId);
        Assert.Contains(typeGuard.Children, node => node.Label == "new RequestTimedOutError" && node.Mark == '-');
        var nullGuard = Assert.Single(mapper.Children, node => node.Label == "if (type == typeof(TimeoutException) && exception.InnerException is null)");
        Assert.Equal('+', nullGuard.Mark);
        var timeout = Assert.Single(nullGuard.Children);
        Assert.Equal("new RequestTimedOutError", timeout.Label);
        Assert.Equal('+', timeout.Mark);
        var status = Assert.Single(result.Trees, node => node.Label == "ErrorsToHttpStatusCodeMapper.Map");
        Assert.Equal('~', status.Mark);
        Assert.Equal("body changed; visible calls unchanged", status.Detail);
    }
}
