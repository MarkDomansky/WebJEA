namespace WebJEA.Auth;

public enum RevalidationStatus
{
    /// <summary>The sign-in is still valid; User carries the groups to authorize with.</summary>
    Valid,

    /// <summary>The sign-in is no longer valid; the user has been signed out.</summary>
    Revoked,

    /// <summary>The sign-in couldn't be checked and the check fails closed.</summary>
    Unavailable
}

/// <param name="User">Who to authorize: current groups when Valid.</param>
/// <param name="SignInUser">The same user as of sign-in (token claims only), to tell when access was removed since.</param>
public sealed record RevalidationResult(RevalidationStatus Status, IUserContext User, IUserContext SignInUser);

/// <summary>
/// Re-checks the signed-in user with the identity provider before a script runs.
/// Registered only in Entra mode with Graph available; endpoints skip the check when
/// no revalidator is registered.
/// </summary>
public interface IUserRevalidator
{
    Task<RevalidationResult> RevalidateAsync(HttpContext context);
}
