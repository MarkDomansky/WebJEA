namespace WebJEA;

/// <summary>Result of <see cref="IAuthorizationService.CheckCommandAccess"/>.</summary>
public enum CommandAccess
{
    Allowed,
    NotFound,
    Denied
}
