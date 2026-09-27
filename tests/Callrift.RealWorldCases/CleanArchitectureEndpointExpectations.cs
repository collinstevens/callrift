using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class CleanArchitectureEndpointExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(470, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(4, result.Diagnostics.Count(diagnostic => diagnostic.Code == "CS1513"));
        Assert.Equal(4, result.Diagnostics.Count(diagnostic => diagnostic.Code == "CS1514"));
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-record-copy"));
        Assert.All(result.Diagnostics.Where(diagnostic => diagnostic.Code != "unresolved-call"), diagnostic =>
            Assert.StartsWith("templates/ca-use-case/", diagnostic.Location!.Path));
        if (!focused)
        {
            Assert.Equal(25, result.Trees.Count);
            var prefix = Assert.Single(result.Trees, node => node.Label == "IEndpointGroup.get_RoutePrefix");
            Assert.Equal('+', prefix.Mark);
            Assert.Equal("public static virtual CleanArchitecture.Web.Infrastructure.IEndpointGroup.get_RoutePrefix() -> string?", prefix.After!.Signature);
            Assert.Empty(prefix.Children);
            foreach (var method in new[] { "TodoItems.UpdateTodoItem", "TodoItems.UpdateTodoItemDetail", "TodoLists.UpdateTodoList" })
            {
                var update = Assert.Single(result.Trees, node => node.Label == method);
                Assert.EndsWith("Command.get_Id", update.Children[0].Label);
                Assert.Equal("if (id != command.Id)", update.Children[1].Label);
                Assert.Equal("? TypedResults.BadRequest", Assert.Single(update.Children[1].Children).Label);
                Assert.Equal("? sender.Send", update.Children[2].Label);
            }
            var program = Assert.Single(result.Trees, node => node.Label == "src/Web/Program.cs::<top-level>");
            Assert.Equal("body changed; visible calls unchanged", program.Detail);
            foreach (var endpoint in new[] { "TodoItems", "TodoLists", "Users", "WeatherForecasts" })
            {
                var constructor = Assert.Single(result.Trees, node => node.Label == "new " + endpoint);
                var removedBase = Assert.Single(constructor.Children);
                Assert.Equal("new EndpointGroupBase", removedBase.Label);
                Assert.Equal('-', removedBase.Mark);
            }
            return;
        }
        Assert.Equal(10, result.Trees.Count);
        foreach (var endpoint in new[] { "TodoItems", "TodoLists", "Users", "WeatherForecasts" })
        {
            var map = Assert.Single(result.Trees, node => node.Label == endpoint + ".Map");
            Assert.Equal('~', map.Mark);
            Assert.Equal(map.Before!.SymbolId, map.After!.SymbolId);
            Assert.StartsWith("public override ", map.Before.Signature);
            Assert.StartsWith("public static ", map.After.Signature);
        }
        var mapping = Assert.Single(result.Trees, node => node.Label == "WebApplicationExtensions.MapEndpoints");
        Assert.Contains("System.Reflection.Assembly assembly", mapping.After!.Signature);
        var mappingCalls = Descendants(mapping.Children).ToArray();
        Assert.Equal('-', Assert.Single(mappingCalls, node => node.Label == "Activator.CreateInstance").Mark);
        var oldDispatch = Assert.Single(mappingCalls, node => node.Label == "EndpointGroupBase.Map");
        Assert.Equal(4, oldDispatch.Children.Count);
        Assert.All(oldDispatch.Children, node =>
        {
            Assert.NotNull(node.Before);
            Assert.Null(node.After);
            Assert.Equal("depth-limit", node.Omission!.Reason);
        });
        Assert.Equal('+', Assert.Single(mappingCalls, node => node.Label == "type.GetProperty").Mark);
        Assert.Equal('+', Assert.Single(mappingCalls, node => node.Label == "type.GetMethod").Mark);
        Assert.DoesNotContain(mappingCalls, node => node.After?.SymbolId?.Contains("IEndpointGroup.Map", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(mappingCalls, node => node.After?.SymbolId?.Contains("IEndpointGroup.get_RoutePrefix", StringComparison.Ordinal) == true);
        var pattern = Assert.Single(mappingCalls, node => node.Label == "pattern (t is { IsAbstract: false, IsInterface: false })");
        Assert.Equal(["Type.get_IsAbstract", "Type.get_IsInterface"], pattern.Children.Select(node => node.Label));
        var oldGroup = Assert.Single(mappingCalls, node => node.Label == "WebApplicationExtensions.MapGroup");
        Assert.Equal("EndpointGroupBase.get_GroupName", oldGroup.Children[0].Label);
        Assert.Equal("if (group.GroupName is null)", oldGroup.Children[1].Label);
        Assert.Equal(["group.GetType", "MemberInfo.get_Name"], oldGroup.Children[1].Children.Select(node => node.Label));
        var removedHandler = Assert.Single(result.Trees, node => node.Label == "CustomExceptionHandler.TryHandleAsync");
        Assert.Null(removedHandler.After);
        var lookup = removedHandler.Children[^1];
        Assert.Equal("if (_exceptionHandlers.ContainsKey(exceptionType))", lookup.Label);
        Assert.Equal(["Dictionary<TKey, TValue>.get_Item", "_exceptionHandlers[exceptionType].Invoke"], lookup.Children.Select(node => node.Label));
        var addedHandler = Assert.Single(result.Trees, node => node.Label == "ProblemDetailsExceptionHandler.TryHandleAsync");
        Assert.Null(addedHandler.Before);
        Assert.Equal(4, addedHandler.Children.Count(node => node.Label.StartsWith("case ", StringComparison.Ordinal)));
        Assert.Equal(["ValidationException.get_Errors", "? new ValidationProblemDetails"], addedHandler.Children[0].Children.Select(node => node.Label));
        Assert.Equal("? httpContext.Response.WriteAsJsonAsync", addedHandler.Children[^1].Label);
        Assert.DoesNotContain(addedHandler.Children, node => node.Label.Contains("problemDetails is null", StringComparison.Ordinal));
        var anonymous = Assert.Single(result.Trees, node => node.Label == "MethodInfoExtensions.IsAnonymous");
        Assert.Equal("MemberInfo.get_Name", anonymous.Children[0].Label);
        Assert.Equal("possible initialization of MethodInfoExtensions", anonymous.Children[1].Label);
        Assert.Equal('+', anonymous.Children[1].Mark);
        var transformer = Assert.Single(result.Trees, node => node.Label == "ApiExceptionOperationTransformer.TransformAsync");
        Assert.Equal(transformer.Before!.SymbolId, transformer.After!.SymbolId);
        Assert.EndsWith("ApiExceptionOperationProcessor.cs", transformer.Before.Definition!.Path);
        Assert.EndsWith("ApiExceptionOperationTransformer.cs", transformer.After.Definition!.Path);
        Assert.Equal('-', Assert.Single(Descendants(transformer.Children), node => node.Label == "? new OpenApiResponses").Mark);
        Assert.Equal("Task.get_CompletedTask", transformer.Children[^1].Label);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
