using System.Security.Claims;

namespace WebJEA.Auth.Entra;

public class EntraUserContextFactory : IUserContextFactory
{
    private readonly IServiceProvider _services;

    public EntraUserContextFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IUserContext Create(ClaimsPrincipal principal)
    {
        // Groups from a recent pre-execute revalidation are newer than the token's, so the
        // menu and command metadata agree with what execute allows.
        var cache = _services.GetService<RevalidationCache>();
        if (cache != null && cache.TryGet(principal, out var groupIds))
        {
            return EntraUserContext.FromGraphGroups(principal, groupIds);
        }

        var graphGroupLoader = _services.GetService<GraphGroupLoader>();
        return new EntraUserContext(principal, graphGroupLoader);
    }
}
