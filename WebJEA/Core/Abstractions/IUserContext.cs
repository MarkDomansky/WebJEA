namespace WebJEA;

/// <summary>
/// Authentication-mode-neutral view of the current user. Windows mode exposes SIDs in
/// <see cref="MemberOfIds"/>; Entra mode exposes group object IDs.
/// </summary>
public interface IUserContext
{
    string UserName { get; }
    IReadOnlyList<string> MemberOfIds { get; }
    bool IsMemberOf(string groupId);

    /// <summary>Anonymous org identifier used for telemetry (domain SID, machine GUID, or tenant id).</summary>
    string OrgId { get; }

    /// <summary>Org display root used for telemetry (domain DNS root, machine name, or tenant domain).</summary>
    string OrgName { get; }
}
