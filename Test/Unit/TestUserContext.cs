using WebJEA;

namespace WebJEA.Tests;

/// <summary>Replaces the old internal UserInfo(sids, username) test constructor.</summary>
public sealed class TestUserContext : IUserContext
{
    private readonly List<string> _ids;

    public TestUserContext(List<string> ids, string userName)
    {
        _ids = ids;
        UserName = userName;
    }

    public string UserName { get; }

    public IReadOnlyList<string> MemberOfIds => _ids;

    public bool IsMemberOf(string groupId)
    {
        if (groupId == "*") return true;
        return _ids.Contains(groupId);
    }

    public string OrgId => "-";

    public string OrgName => "-";
}
