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
        var graphGroupLoader = _services.GetService<GraphGroupLoader>();
        return new EntraUserContext(principal, graphGroupLoader);
    }
}
