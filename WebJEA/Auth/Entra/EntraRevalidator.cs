using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace WebJEA.Auth.Entra;

/// <summary>
/// Pre-execute check in Entra mode: one delegated Graph call (/me/transitiveMemberOf)
/// both proves the sign-in is still valid and returns the user's current groups. WebJEA
/// declares the CAE client capability (cp1), so Graph rejects the token in near real time
/// once the user is disabled, their sessions are revoked, or their password is reset.
/// A revoked sign-in signs the user out. When Graph can't be checked, execute is refused
/// unless Authentication:Entra:RevalidateFailOpen is true.
/// </summary>
public class EntraRevalidator : IUserRevalidator
{
    private readonly NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private readonly GraphGroupLoader _graph;
    private readonly RevalidationCache _cache;
    private readonly bool _failOpen;

    public EntraRevalidator(GraphGroupLoader graph, RevalidationCache cache, IConfiguration config)
    {
        _graph = graph;
        _cache = cache;
        _failOpen = config.GetValue("Authentication:Entra:RevalidateFailOpen", false);
    }

    public async Task<RevalidationResult> RevalidateAsync(HttpContext context)
    {
        var principal = context.User;
        var signInUser = EntraUserContext.FromTokenOnly(principal);

        // An app-only bearer token has no user to look up in Graph; it was validated on this
        // request and an app's access ends when its token expires.
        if (EntraPrincipal.IsAppOnly(principal))
        {
            dlog.Trace("Entra: " + signInUser.UserName + " is an app-only caller; no sign-in revalidation.");
            return new RevalidationResult(RevalidationStatus.Valid, signInUser, signInUser);
        }

        if (_cache.TryGet(principal, out var cached))
        {
            return new RevalidationResult(RevalidationStatus.Valid, EntraUserContext.FromGraphGroups(principal, cached), signInUser);
        }

        GroupLookup lookup = _graph.LoadTransitiveGroupIdsForRevalidation();
        switch (lookup.Status)
        {
            case GroupLookupStatus.Ok:
                _cache.Set(principal, lookup.Ids);
                return new RevalidationResult(RevalidationStatus.Valid, EntraUserContext.FromGraphGroups(principal, lookup.Ids), signInUser);

            case GroupLookupStatus.SignInRequired:
                _cache.Remove(principal);
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                dlog.Warn("Entra: signed out " + signInUser.UserName + " before execute: sign-in no longer valid (" + lookup.Detail + ").");
                return new RevalidationResult(RevalidationStatus.Revoked, signInUser, signInUser);

            default:
                if (_failOpen)
                {
                    // Today's behavior: token claims, plus the overage lookup (which fails open too)
                    var tokenUser = new EntraUserContext(principal, _graph);
                    dlog.Warn("Entra: couldn't revalidate " + signInUser.UserName + " (" + lookup.Detail +
                              "); RevalidateFailOpen is true, so authorizing on sign-in claims.");
                    return new RevalidationResult(RevalidationStatus.Valid, tokenUser, signInUser);
                }

                dlog.Warn("Entra: couldn't revalidate " + signInUser.UserName + " (" + lookup.Detail +
                          "); execute refused. Set Authentication:Entra:RevalidateFailOpen to true to run on sign-in claims instead.");
                return new RevalidationResult(RevalidationStatus.Unavailable, signInUser, signInUser);
        }
    }
}
