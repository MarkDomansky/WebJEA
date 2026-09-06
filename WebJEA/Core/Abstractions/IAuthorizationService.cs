using System.Data;

namespace WebJEA;

public interface IAuthorizationService
{
    void InitGroups(IConfigProvider config, IGroupResolver grpfinder);
    bool IsGlobalUser(IUserContext uinfo);
    bool IsCommandAvailable(IUserContext uinfo, string commandId);
    ConfigCmd GetCommand(IUserContext uinfo, string commandId);
    List<MenuItem> GetMenu(IUserContext uinfo);

    [Obsolete("Server-side menu rendering was replaced by the /api/menu endpoint.")]
    DataTable GetMenuDataTable(IUserContext uinfo, string activeID);
}
