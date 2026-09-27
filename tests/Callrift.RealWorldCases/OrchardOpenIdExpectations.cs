using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OrchardOpenIdExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        if (restored) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(17239, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(19, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
            Assert.Equal(6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        }
        if (!focused)
        {
            Assert.Equal(restored ? 8 : 345, result.Trees.Count);
            if (restored)
            {
                var recipe = Assert.Single(result.Trees, node => node.Label == "NamedRecipeStepHandler.ExecuteAsync");
                Assert.Equal("RecipeExecutionContext.get_Name", recipe.Children[0].Label);
                Assert.Equal("NamedRecipeStepHandler.HandleAsync", recipe.Children[1].Label);
                Assert.Contains(recipe.Children[1].Children, node => node.Label == "⇢ OpenIdServerSettingsStep.HandleAsync");
            }
            else
            {
                var helper = Assert.Single(result.Trees, node => node.Label == "AccessController.SignOutAndRedirectAsync");
                Assert.Equal('+', helper.Mark);
                Assert.Contains(helper.Children, node => node.Label == "? HttpContext.SignOutAsync");
                var anonymousReads = Descendants(result.Trees).Where(node => node.Label.StartsWith("<anonymous type:", StringComparison.Ordinal)).ToArray();
                Assert.Equal(4, anonymousReads.Length);
                Assert.All(anonymousReads, node => Assert.Contains("IndexProfileKey Source, String ProviderName", node.Label));
            }
            return;
        }
        Assert.Equal(2, result.Trees.Count);
        var logout = Assert.Single(result.Trees, node => node.Label == "AccessController.Logout");
        var labels = logout.Children.Select(node => node.Label).ToList();
        Assert.Equal("if (!settings.RequireEndSessionConfirmation)", labels[labels.IndexOf("OpenIdServerSettings.get_RequireEndSessionConfirmation") + 1]);
        var hint = Assert.Single(logout.Children, node => node.Label == "if (!settings.RequireEndSessionConfirmation && !string.IsNullOrEmpty(request.IdTokenHint))");
        var comparison = Assert.Single(hint.Children, node => node.Label == "if (!string.IsNullOrEmpty(hintSubject) && !string.IsNullOrEmpty(userIdentifier))");
        Assert.Equal([restored ? "hintSubject.AsSpan" : "? hintSubject.AsSpan", restored ? "MemoryMarshal.AsBytes<char>" : "? MemoryMarshal.AsBytes<char>",
            "userIdentifier.AsSpan", "MemoryMarshal.AsBytes<char>", restored ? "CryptographicOperations.FixedTimeEquals" : "? CryptographicOperations.FixedTimeEquals"],
            comparison.Children.Select(node => node.Label));
        var accept = Assert.Single(result.Trees, node => node.Label == "AccessController.LogoutAccept");
        var helperCall = Assert.Single(accept.Children, node => node.Mark == '+' && node.Label.EndsWith("SignOutAndRedirectAsync", StringComparison.Ordinal));
        if (!restored)
        {
            Assert.Equal("? SignOutAndRedirectAsync", helperCall.Label);
            Assert.Empty(helperCall.Children);
            Assert.Contains(hint.Children, node => node.Label == "? result.Principal.FindUserIdentifier");
            return;
        }
        Assert.Equal(["ControllerBase.get_HttpContext", "HttpContext.AuthenticateAsync"], hint.Children.Take(2).Select(node => node.Label));
        var oldPattern = Assert.Single(hint.Children, node => node.Label == "pattern (hintResult is { Succeeded: true, Principal: not null })");
        Assert.Equal('-', oldPattern.Mark);
        Assert.Equal(["AuthenticateResult.get_Succeeded", "AuthenticateResult.get_Principal"], oldPattern.Children.Select(node => node.Label));
        var newPattern = Assert.Single(hint.Children, node => node.Label == "pattern (hintResult is { Succeeded: true })");
        Assert.Equal('+', newPattern.Mark);
        Assert.Equal("AuthenticateResult.get_Succeeded", Assert.Single(newPattern.Children).Label);
        var identifier = Assert.Single(hint.Children, node => node.Label == "OpenIdExtensions.FindUserIdentifier");
        Assert.Equal("AuthenticateResult.get_Principal", hint.Children[hint.Children.ToList().IndexOf(identifier) - 1].Label);
        Assert.Equal(3, Descendants(identifier.Children).Count(node => node.Label == "principal.FindFirst"));
        Assert.Equal(3, Descendants(identifier.Children).Count(node => node.Label == "Claim.get_Value"));
        Assert.All(Descendants(identifier.Children).Where(node => node.Children.Any(child => child.Label == "Claim.get_Value")),
            node => Assert.EndsWith("is not null)", node.Label));
        var boundedHelper = Assert.Single(Descendants(hint.Children), node => node.Label == "AccessController.SignOutAndRedirectAsync");
        Assert.Equal("depth-limit", boundedHelper.Omission!.Reason);
        Assert.Equal(accept.Children.Where(node => node.Mark == '-').Select(node => node.Label), helperCall.Children.Select(node => node.Label));
        Assert.Equal(["ControllerBase.get_HttpContext", "HttpContext.SignOutAsync", "OpenIddictRequest.get_PostLogoutRedirectUri", "string.IsNullOrEmpty",
            "if (string.IsNullOrEmpty(request.PostLogoutRedirectUri))", "new AuthenticationProperties", "AuthenticationProperties.set_RedirectUri", "SignOut"],
            helperCall.Children.Select(node => node.Label));
        Assert.Equal("Redirect", Assert.Single(helperCall.Children[4].Children).Label);
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
