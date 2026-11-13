using System.Security.Claims;

namespace WebJEA.Auth.Entra;

/// <summary>
/// Entra ID user context. MemberOfIds carries the group object IDs from the "groups"
/// claims and the app role values from the "roles" claims (all lowercased), plus the
/// user's own object ID and UPN so individual users can be granted access directly in
/// PermittedGroups.
/// </summary>
public class EntraUserContext : IUserContext
{
    private static readonly string[] GroupClaimTypes =
    {
        "groups",
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups"
    };

    private static readonly string[] RoleClaimTypes =
    {
        "roles",
        ClaimTypes.Role
    };

    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private readonly List<string> _ids = new List<string>();
    private readonly string _tenantId;

    public EntraUserContext(ClaimsPrincipal principal, GraphGroupLoader graphGroupLoader)
        : this(principal, null, graphGroupLoader, warnOnOverage: true)
    {
    }

    /// <summary>
    /// Groups from a fresh Graph lookup instead of the sign-in token's groups claim;
    /// roles, object ID and UPN still come from the token.
    /// </summary>
    internal static EntraUserContext FromGraphGroups(ClaimsPrincipal principal, IEnumerable<string> groupIds)
    {
        return new EntraUserContext(principal, groupIds, null, warnOnOverage: false);
    }

    /// <summary>
    /// The sign-in token's claims only: no overage lookup and no overage warning. Used to
    /// tell whether access was removed since sign-in.
    /// </summary>
    internal static EntraUserContext FromTokenOnly(ClaimsPrincipal principal)
    {
        return new EntraUserContext(principal, null, null, warnOnOverage: false);
    }

    private EntraUserContext(ClaimsPrincipal principal, IEnumerable<string> freshGroupIds,
                             GraphGroupLoader graphGroupLoader, bool warnOnOverage)
    {
        UserName = FirstClaim(principal, "preferred_username", ClaimTypes.Upn, ClaimTypes.Name)
                   ?? principal.Identity?.Name ?? "";
        _tenantId = FirstClaim(principal, "tid", "http://schemas.microsoft.com/identity/claims/tenantid") ?? "-";

        if (freshGroupIds != null)
        {
            _ids.AddRange(freshGroupIds.Select(id => id.ToLowerInvariant()));
        }
        else
        {
            AddTokenGroups(principal, graphGroupLoader, warnOnOverage);
        }

        foreach (string claimType in RoleClaimTypes)
        {
            foreach (Claim claim in principal.FindAll(claimType))
            {
                _ids.Add(claim.Value.ToLowerInvariant());
            }
        }

        string objectId = FirstClaim(principal, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier");
        if (!string.IsNullOrEmpty(objectId))
        {
            _ids.Add(objectId.ToLowerInvariant());
        }

        if (!string.IsNullOrEmpty(UserName))
        {
            _ids.Add(UserName.ToLowerInvariant());
        }
    }

    private void AddTokenGroups(ClaimsPrincipal principal, GraphGroupLoader graphGroupLoader, bool warnOnOverage)
    {
        int groupClaimCount = 0;
        foreach (string claimType in GroupClaimTypes)
        {
            foreach (Claim claim in principal.FindAll(claimType))
            {
                _ids.Add(claim.Value.ToLowerInvariant());
                groupClaimCount++;
            }
        }

        // Group overage: with >200 groups Entra omits the groups claim and emits a
        // _claim_names/_claim_sources (or hasgroups) marker instead. Fall back to Graph.
        if (groupClaimCount == 0 && HasGroupOverage(principal))
        {
            if (graphGroupLoader != null)
            {
                dlog.Trace("EntraUserContext: group overage detected; querying Graph transitiveMemberOf");
                _ids.AddRange(graphGroupLoader.GetTransitiveGroupIds().Select(id => id.ToLowerInvariant()));
            }
            else if (warnOnOverage)
            {
                dlog.Warn("EntraUserContext: group overage detected for " + UserName +
                          " but Authentication:Entra:EnableGraph is false; group authorization will fail.");
            }
        }
    }

    private static string FirstClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrEmpty(value)) return value;
        }

        return null;
    }

    private static bool HasGroupOverage(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == "hasgroups")) return true;

        string claimNames = principal.FindFirst("_claim_names")?.Value;
        return claimNames != null && claimNames.Contains("groups");
    }

    public string UserName { get; }

    public IReadOnlyList<string> MemberOfIds => _ids;

    public bool IsMemberOf(string groupId)
    {
        if (groupId == "*") return true;
        return _ids.Contains(groupId);
    }

    public string OrgId => _tenantId;

    public string OrgName
    {
        get
        {
            int idx = UserName.IndexOf('@');
            return idx > -1 ? UserName.Substring(idx + 1) : "-";
        }
    }
}
