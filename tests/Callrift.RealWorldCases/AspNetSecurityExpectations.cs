using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class AspNetSecurityExpectations
{
    public static void VerifyAuthorization(DiffResult result, bool focused)
    {
        VerifyCoverage(result);
        if (!focused)
        {
            Assert.True(result.Truncated);
            Assert.Equal(1179, result.Trees.Count);
            Assert.Equal(3, result.Trees.Count(node => node.Label == "AuthorizationServiceExtensions.AuthorizeAsync"));
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("LoggingExtensions.UserAuthorizationFailed", root.Label);
        Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
        Assert.Equal(root.Before.Signature, root.After.Signature);
        var explicitFailure = Assert.Single(root.Children, node => node.Label == "if (failure.FailCalled)");
        Assert.Equal('+', explicitFailure.Mark);
        Assert.Contains(explicitFailure.Children, node => node.Label == "failure.FailureReasons.Any" && node.Mark == '+');
        var reasons = Assert.Single(explicitFailure.Children, node => node.Label == "if (failure.FailureReasons.Any())");
        Assert.Equal(["failure.FailureReasons.Select", "string.Join"], reasons.Children.Select(node => node.Label));
        Assert.All(reasons.Children, node => Assert.Equal('+', node.Mark));
        var requirements = Assert.Single(root.Children, node => node.Label == "else (!(failure.FailCalled))");
        Assert.Equal(' ', requirements.Mark);
        Assert.Equal("string.Join", Assert.Single(requirements.Children).Label);
        var logger = Assert.Single(root.Children, node => node.Kind == "call");
        Assert.Equal(' ', logger.Mark);
        Assert.NotEqual(root.After.SymbolId, logger.After!.SymbolId);
        Assert.EndsWith(",string)", logger.After.SymbolId);
        Assert.Contains("private static partial", logger.After.Signature);
    }

    public static void VerifySignOut(DiffResult result, bool focused)
    {
        VerifyCoverage(result);
        Assert.True(result.Truncated);
        if (!focused)
        {
            Assert.Equal(3, result.Trees.Count);
            Assert.Equal(["Startup.ConfigureServices", "IdentityServiceCollectionExtensions.AddIdentityApiEndpoints", "IdentityServiceCollectionUIExtensions.AddDefaultIdentity"],
                result.Trees.Select(node => node.Label));
            Assert.Equal("src/Security/samples/Identity.ExternalClaims/Startup.cs", result.Trees[0].After!.Definition!.Path);
            return;
        }
        var root = Assert.Single(result.Trees);
        Assert.Equal("SecurityStampValidator<TUser>.ValidateAsync", root.Label);
        Assert.Equal(root.Before!.Signature, root.After!.Signature);
        var failed = Assert.Single(Descendants(root.Children), node => node.Label == "else (!(user != null))");
        Assert.Contains(failed.Children, node => node.Label == "CookieValidatePrincipalContext.RejectPrincipal" && node.Mark == ' ');
        Assert.Contains(failed.Children, node => node.Label == "SignInManager<TUser>.SignOutAsync" && node.Mark == ' ');
        Assert.Contains(failed.Children, node => node.Mark == '+' && node.Label == "context.HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>");
        var lookup = Assert.Single(failed.Children, node => node.After?.SymbolId == "source::Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider.GetSchemeAsync(string)");
        Assert.Equal('+', lookup.Mark);
        Assert.Equal("possible", lookup.After!.Dispatch);
        Assert.Equal(["source::Microsoft.AspNetCore.Authentication.AuthenticationSchemeProvider.GetSchemeAsync(string)"], lookup.After.TargetIds);
        var guard = Assert.Single(failed.Children, node => node.Label == "if (await schemes.GetSchemeAsync(IdentityConstants.TwoFactorRememberMeScheme) != null)");
        Assert.Equal('+', guard.Mark);
        var added = Assert.Single(guard.Children);
        var removed = Assert.Single(failed.Children, node => node.Mark == '-');
        Assert.Equal("AuthenticationHttpContextExtensions.SignOutAsync", added.Label);
        Assert.Equal('+', added.Mark);
        Assert.Equal(removed.Before!.SymbolId, added.After!.SymbolId);
        Assert.Equal(160, Assert.Single(removed.Before.CallSites).Line);
        Assert.Equal(164, Assert.Single(added.After.CallSites).Line);
    }

    private static void VerifyCoverage(DiffResult result)
    {
        Assert.Equal("source", result.Coverage.Mode);
        Assert.Equal("partial", result.Coverage.Status);
        Assert.Contains("possible-dispatch", result.Coverage.Limitations);
        Assert.Contains("unfollowed-accessors-operators-events", result.Coverage.Limitations);
        Assert.Equal(22750, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
        Assert.Equal(82, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
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
