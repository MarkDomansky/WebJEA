using System.Security.Claims;

namespace WebJEA.Auth;

/// <summary>
/// Claims-based user context used for the Development auto-login principal
/// (and any non-Windows identity in Windows mode). MemberOfIds carries every
/// claim value, mirroring the old UserInfo behavior of matching on both the
/// user name and group identifiers.
/// </summary>
public class ClaimsUserContext : IUserContext
{
    private readonly List<string> _ids;

    public ClaimsUserContext(ClaimsPrincipal principal)
    {
        UserName = principal.Identity?.Name ?? "";
        _ids = principal.Claims.Select(c => c.Value).ToList();
    }

    public string UserName { get; }

    public IReadOnlyList<string> MemberOfIds => _ids;

    public bool IsMemberOf(string groupId)
    {
        if (groupId == "*") return true;
        return _ids.Contains(groupId);
    }

    public string OrgId => "-";

    public string OrgName => Environment.MachineName;
}
