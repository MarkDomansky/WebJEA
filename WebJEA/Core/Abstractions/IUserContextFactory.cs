using System.Security.Claims;

namespace WebJEA;

public interface IUserContextFactory
{
    IUserContext Create(ClaimsPrincipal principal);
}
