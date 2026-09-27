using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class CleanArchitectureLegacyExpectations
{
    public static void Verify(DiffResult result, string id)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Equal(id switch
        {
            "cleanarchitecture-endpoint-groups" => 443,
            "cleanarchitecture-logging-di" => 367,
            "cleanarchitecture-validation-lambda" => 364,
            "cleanarchitecture-handler-rename" => 358,
            "cleanarchitecture-guard-library" => 900,
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        }, result.Diagnostics.Count);
        foreach (var entity in new[] { "TodoList", "TodoItem" })
        {
            var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message == $"Cannot bind Set<{entity}>.");
            Assert.Equal("unresolved-call", diagnostic.Code);
            Assert.EndsWith("/ApplicationDbContext.cs", diagnostic.Location!.Path);
        }
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message == "Cannot bind .FindFirstValue.");
        if (id != "cleanarchitecture-guard-library")
        {
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message == "Cannot bind .FindAll.");
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message == "Cannot bind .FindAll(ClaimTypes.Role).Select.");
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message == "Cannot bind .FindAll(ClaimTypes.Role).Select(x => x.Value).ToList.");
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("DeleteTodoItemCommandHandler.Handle", root.Label);
        Assert.Equal("IApplicationDbContext.get_TodoItems → ApplicationDbContext.get_TodoItems", root.Children[0].Label);
        Assert.Equal("possible", root.Children[0].After!.Dispatch);
        Assert.EndsWith("::CleanArchitecture.Infrastructure.Persistence.ApplicationDbContext.get_TodoItems()",
            Assert.Single(root.Children[0].After!.TargetIds));
        Assert.Equal("? Set<TodoItem>", Assert.Single(root.Children[0].Children).Label);
        Assert.Equal("DeleteTodoItemCommand.get_Id", root.Children[1].Label);
        Assert.Equal("? _context.TodoItems.FindAsync", root.Children[2].Label);
        var removedGuard = root.Children[3];
        Assert.Equal("if (entity == null)", removedGuard.Label);
        Assert.Equal('-', removedGuard.Mark);
        Assert.Equal(["DeleteTodoItemCommand.get_Id", "new NotFoundException"], removedGuard.Children.Select(node => node.Label));
        Assert.Equal("DeleteTodoItemCommand.get_Id", root.Children[4].Label);
        Assert.Equal('+', root.Children[4].Mark);
        Assert.Equal("? Guard.Against.NotFound", root.Children[5].Label);
        Assert.Equal('+', root.Children[5].Mark);
        Assert.Equal(root.Children[0].Label, root.Children[6].Label);
        Assert.Equal("? _context.TodoItems.Remove", root.Children[7].Label);
        Assert.Equal("? new TodoItemDeletedEvent", root.Children[8].Label);
        Assert.Equal(' ', root.Children[8].Mark);
        Assert.Equal("? entity.AddDomainEvent", root.Children[9].Label);
        Assert.Equal("IApplicationDbContext.SaveChangesAsync → ApplicationDbContext.SaveChangesAsync", root.Children[10].Label);
    }
}
