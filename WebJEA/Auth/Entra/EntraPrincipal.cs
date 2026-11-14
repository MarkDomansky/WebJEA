using System.Security.Claims;

namespace WebJEA.Auth.Entra;

/// <summary>
/// Tells app-only callers (client-credentials bearer tokens) apart from signed-in users.
/// The bearer scheme marks app-only principals with a claim WebJEA issues itself, so a
/// claim of the same name inside a token is never trusted.
/// </summary>
public static class EntraPrincipal
{
    public const string AppOnlyClaimType = "webjea_apponly";

    /// <summary>The UserName prefix for app-only callers, e.g. "app:&lt;client-id&gt;".</summary>
    public const string AppUserNamePrefix = "app:";

    private static readonly string[] ScopeClaimTypes =
    {
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope"
    };

    public static bool IsAppOnly(ClaimsPrincipal principal)
    {
        return principal?.HasClaim(c => c.Type == AppOnlyClaimType && c.Issuer == ClaimsIdentity.DefaultIssuer) == true;
    }

    /// <summary>True when the token was issued to a user (delegated), not to an app.</summary>
    public static bool HasDelegatedScopes(ClaimsPrincipal principal)
    {
        return ScopeClaimTypes.Any(t => principal.HasClaim(c => c.Type == t));
    }

    /// <summary>The calling app's client ID: "azp" in v2 tokens, "appid" in v1 tokens.</summary>
    public static string AppId(ClaimsPrincipal principal)
    {
        string appId = principal.FindFirst("azp")?.Value;
        if (string.IsNullOrEmpty(appId)) appId = principal.FindFirst("appid")?.Value;
        return appId ?? "";
    }

    /// <summary>
    /// Marks a validated app-only bearer principal: drops any same-named claim the token
    /// carried, then adds WebJEA's own marker and an "app:&lt;client-id&gt;" name for logging.
    /// </summary>
    internal static void MarkAppOnly(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity) return;

        foreach (Claim forged in identity.FindAll(AppOnlyClaimType).ToList())
        {
            identity.RemoveClaim(forged);
        }

        identity.AddClaim(new Claim(AppOnlyClaimType, "true"));

        if (string.IsNullOrEmpty(identity.Name))
        {
            identity.AddClaim(new Claim(identity.NameClaimType, AppUserNamePrefix + AppId(principal)));
        }
    }
}
