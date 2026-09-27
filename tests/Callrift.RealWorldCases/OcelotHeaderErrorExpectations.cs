using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OcelotHeaderErrorExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Contains("callbacks-are-possible-calls", result.Coverage.Limitations);
        if (restored) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(1040, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("unresolved-call", diagnostic.Code));
        }

        if (!focused)
        {
            Assert.Equal(restored ? 44 : 68, result.Trees.Count);
            Assert.Contains(result.Trees, node => node.Label == "HttpRequesterMiddleware.Invoke");
            var exception = Assert.Single(result.Trees, node => node.Label == "Error.get_Exception");
            Assert.Equal('+', exception.Mark);
            Assert.Null(exception.Before);
            Assert.Equal("public Ocelot.Errors.Error.get_Exception() -> System.Exception", exception.After!.Signature);
            Assert.Contains(result.Trees, node => node.Label == (restored
                ? "ResponderMiddleware.Invoke" : "samples/Metadata/Program.cs::<top-level>"));
            return;
        }

        Assert.Equal(6, result.Trees.Count);
        Assert.All(result.Trees, root =>
        {
            Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
            Assert.Equal(root.Before.Signature, root.After.Signature);
        });
        var constructor = Root(result, "new HttpDataRepository");
        var guard = Assert.Single(constructor.Children, node => node.Mark == '+');
        Assert.EndsWith("ArgumentNullException.ThrowIfNull", guard.Label);
        Assert.Equal(12, Assert.Single(guard.After!.CallSites).Line);
        Assert.Equal(restored ? "resolved" : "unresolved", guard.After.Binding);
        VerifyRepositoryCatch(Root(result, "HttpDataRepository.Add"), restored, 25);
        VerifyRepositoryCatch(Root(result, "HttpDataRepository.Update"), restored, 38);

        var get = Root(result, "HttpDataRepository.Get");
        Assert.Contains(get.Children, node => node.Label == "if (_contextAccessor?.HttpContext?.Items == null)" && node.Mark == '-');
        Assert.Contains(get.Children, node => node.Label == "if (_contextAccessor.HttpContext?.Items == null)" && node.Mark == '+');
        if (restored)
        {
            Assert.Contains(get.Children, node => node.Label == "if (_contextAccessor is not null)" && node.Mark == '-');
            Assert.Contains(get.Children, node => node.Label == "if (_contextAccessor.HttpContext is not null)" && node.Mark == '+');
            var renamed = Assert.Single(Descendants(get.Children), node => node.Label == "new Error" && node.Mark == '~');
            Assert.Equal(renamed.Before!.SymbolId, renamed.After!.SymbolId);
            Assert.Contains("int httpStatusCode", renamed.Before.Signature);
            Assert.Contains("int statusCode", renamed.After.Signature);
        }

        var mapper = Root(result, "HttpExceptionToErrorMapper.Map");
        var typeGuard = Assert.Single(mapper.Children, node => node.Label == "if (exception is HttpRequestException)");
        Assert.Equal('+', typeGuard.Mark);
        Assert.Equal(new[] { "Exception.get_Message", "exception.Message.Contains" }, typeGuard.Children.Select(node => node.Label));
        Assert.All(typeGuard.Children, node => Assert.Equal(45, Assert.Single(node.After!.CallSites).Line));
        var messageGuard = Assert.Single(mapper.Children, node => node.Label.StartsWith("if (exception is HttpRequestException &&", StringComparison.Ordinal));
        var badRequest = Assert.Single(messageGuard.Children);
        Assert.Equal("new BadRequestError", badRequest.Label);
        Assert.Equal('+', badRequest.Mark);
        Assert.Equal(47, Assert.Single(badRequest.After!.CallSites).Line);
        Assert.Contains("System.Exception exception", badRequest.After.Signature);
        var error = Assert.Single(badRequest.Children);
        Assert.Equal(restored ? "resolved" : "unresolved", error.After!.Binding);
        if (restored)
            Assert.Contains(error.Children, node => node.Label == "Exception.get_Message" && node.Mark == '+');
        Assert.Contains(mapper.Children, node => node.Label == "if (_mappers != null && _mappers.TryGetValue(type, out var mapper))" && node.Mark == ' ');
        Assert.Contains(mapper.Children, node => node.Label == "if (type == typeof(TimeoutException))" && node.Mark == ' ');

        var status = Root(result, "ErrorsToHttpStatusCodeMapper.Map");
        Assert.Equal(10, status.Children.Count(node => node.Label == "errors.Any"));
        var predicate = Assert.Single(status.Children, node => node.Mark == '+');
        Assert.Equal("errors.Any", predicate.Label);
        Assert.Equal(64, Assert.Single(predicate.After!.CallSites).Line);
        var code = Assert.Single(predicate.Children);
        Assert.Equal("Error.get_Code", code.Label);
        Assert.Equal("callback", code.After!.Relation);
        Assert.Equal('+', code.Mark);
        Assert.DoesNotContain(status.Children, node => node.Label.Contains("BadRequestError", StringComparison.Ordinal));
    }

    private static void VerifyRepositoryCatch(DiffNode root, bool restored, int line)
    {
        var catchBranch = Assert.Single(root.Children, node => node.Label == "catch (Exception exception)");
        Assert.Contains(catchBranch.Children, node => node.Label == "Exception.get_Message" && node.Mark == '-');
        Assert.Contains(catchBranch.Children, node => node.Label == "string.Format" && node.Mark == '-');
        var error = Assert.Single(catchBranch.Children, node => node.Label == "new CannotAddDataError");
        Assert.Equal('~', error.Mark);
        Assert.NotEqual(error.Before!.SymbolId, error.After!.SymbolId);
        Assert.Contains("string message", error.Before.Signature);
        Assert.Contains("System.Exception exception", error.After.Signature);
        Assert.Equal(line, Assert.Single(error.After.CallSites).Line);
        if (restored)
        {
            var baseError = Assert.Single(error.Children);
            Assert.Equal("new Error", baseError.Label);
            Assert.NotEqual(baseError.Before!.SymbolId, baseError.After!.SymbolId);
            var message = Assert.Single(baseError.Children, node => node.Label == "Exception.get_Message");
            Assert.Equal('+', message.Mark);
            Assert.Equal(16, Assert.Single(message.After!.CallSites).Line);
        }
        else
            Assert.Contains(error.Children, node => node.Mark == '+' && node.After!.Binding == "unresolved");
    }

    private static DiffNode Root(DiffResult result, string label) => Assert.Single(result.Trees, node => node.Label == label);

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
