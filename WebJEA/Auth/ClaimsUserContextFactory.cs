using System.Security.Claims;

namespace WebJEA.Auth;

public class ClaimsUserContextFactory : IUserContextFactory
{
    public IUserContext Create(ClaimsPrincipal principal)
    {
        return new ClaimsUserContext(principal);
    }
}
