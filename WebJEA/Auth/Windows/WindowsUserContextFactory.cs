using System.Security.Claims;
using System.Security.Principal;

namespace WebJEA.Auth.Windows;

public class WindowsUserContextFactory : IUserContextFactory
{
    public IUserContext Create(ClaimsPrincipal principal)
    {
        if (OperatingSystem.IsWindows() && principal.Identity is WindowsIdentity)
        {
            return new WindowsUserContext(principal);
        }

        return new ClaimsUserContext(principal);
    }
}
