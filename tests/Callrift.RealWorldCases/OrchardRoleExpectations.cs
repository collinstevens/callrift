using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OrchardRoleExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Trees.Count);
        if (restored) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(17199, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(19, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
            Assert.Equal(6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        }
        Assert.All(result.Trees, root =>
        {
            Assert.Equal('~', root.Mark);
            Assert.NotEqual(root.Before!.SymbolId, root.After!.SymbolId);
            Assert.DoesNotContain("string submit", root.Before.Signature!);
            Assert.Contains("string submit)", root.After.Signature!);
            var guard = Assert.Single(Descendants(root.Children), node => node.Mark == '+' && node.Label.StartsWith("if (submit == ", StringComparison.Ordinal));
            Assert.Equal("Role.get_RoleName", guard.Children[0].Label);
            Assert.All(guard.Children, node => Assert.Equal('+', node.Mark));
            if (restored && !focused) Assert.Single(guard.Children);
            else Assert.Equal(["Role.get_RoleName", restored ? "RedirectToAction" : "? RedirectToAction"], guard.Children.Select(node => node.Label));
            Assert.DoesNotContain(Descendants(root.Children), node => node.Label.StartsWith("<anonymous type:", StringComparison.Ordinal));
        });
        var create = Assert.Single(result.Trees, node => node.Label == "AdminController.Create");
        var validation = Assert.Single(create.Children, node => node.Label == "if (ModelState.IsValid)");
        Assert.Equal(["CreateRoleViewModel.get_RoleName", "AdminController.ValidateRoleNameAsync"], validation.Children.Select(node => node.Label));
        var valid = Assert.Single(create.Children, node => node.Label == "if (ModelState.IsValid && await ValidateRoleNameAsync(model.RoleName))");
        Consecutive(valid, "CreateRoleViewModel.get_RoleName", "Role.set_RoleName", "CreateRoleViewModel.get_RoleDescription", "Role.set_RoleDescription");
        var success = Assert.Single(valid.Children, node => node.Label == "if (result.Succeeded)");
        var configure = Assert.Single(success.Children, node => node.Label == "if (submit == SaveAndConfigureSubmitValue)");
        Assert.EndsWith("SuccessAsync", success.Children[success.Children.ToList().IndexOf(configure) - 1].Label);
        var edit = Assert.Single(result.Trees, node => node.Label == "AdminController.EditPost");
        Consecutive(edit, "Role.set_RoleDescription", "Role.get_RoleName", "IRoleService.IsAdminRoleAsync → RoleService.IsAdminRoleAsync");
        var claims = Assert.Single(edit.Children, node => node.Label == "if (!await _roleService.IsAdminRoleAsync(role.RoleName))");
        Consecutive(claims, "Role.get_RoleClaims", "role.RoleClaims.RemoveAll", "Role.get_RoleClaims", "AdminController.ExtractSelectedPermissions");
        Assert.Equal("RoleClaim.get_ClaimType", Assert.Single(claims.Children[1].Children).Label);
        var continueGuard = Assert.Single(edit.Children, node => node.Label == "if (submit == SaveAndContinueSubmitValue)");
        Assert.EndsWith("SuccessAsync", edit.Children[edit.Children.ToList().IndexOf(continueGuard) - 1].Label);
        if (!restored || focused)
        {
            Assert.Equal(restored ? "RedirectToAction" : "? RedirectToAction", success.Children[^1].Label);
            Assert.Equal(' ', success.Children[^1].Mark);
            Assert.Equal(restored ? "RedirectToAction" : "? RedirectToAction", edit.Children[^1].Label);
            Assert.Equal(' ', edit.Children[^1].Mark);
        }
        if (!restored || !focused) return;
        Consecutive(create, "ControllerBase.get_ModelState", "ModelStateDictionary.get_IsValid", "if (ModelState.IsValid)");
        Consecutive(valid, "_roleManager.CreateAsync", "IdentityResult.get_Succeeded", "if (result.Succeeded)");
        Consecutive(valid, "IdentityResult.get_Errors", "foreach (var error in result.Errors)");
        var errors = Assert.Single(valid.Children, node => node.Label == "foreach (var error in result.Errors)");
        Assert.Equal(["ControllerBase.get_ModelState", "IdentityError.get_Description", "ModelState.AddModelError"], errors.Children.Select(node => node.Label));
        var selected = Assert.Single(claims.Children, node => node.Label == "AdminController.ExtractSelectedPermissions");
        Consecutive(selected, "ControllerBase.get_Request", "HttpRequest.get_Form", "IFormCollection.get_Keys", "Request.Form.Keys.Where");
        var checkbox = Assert.Single(Descendants(selected.Children), node => node.Label == "if (key.StartsWith(\"Checkbox.\", StringComparison.Ordinal))");
        Assert.Equal(["ControllerBase.get_Request", "HttpRequest.get_Form", "IFormCollection.get_Item", "StringValues.op_Equality"], checkbox.Children.Select(node => node.Label));
    }

    private static void Consecutive(DiffNode parent, params string[] labels)
    {
        var children = parent.Children.Select(node => node.Label).ToList();
        var start = children.IndexOf(labels[0]);
        Assert.True(start >= 0);
        Assert.Equal(labels, children.Skip(start).Take(labels.Length));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
