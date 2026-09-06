using System.Data;

namespace WebJEA;

public class AuthorizationService : IAuthorizationService
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();
    private IConfigProvider _config;
    private List<string> _globalGroupSIDs = new List<string>();
    private Dictionary<string, List<string>> _commandGroupSIDs = new Dictionary<string, List<string>>();
    private readonly bool _allowWildcardGlobal;

    public AuthorizationService() : this(false)
    {
    }

    /// <summary>
    /// <paramref name="allowWildcardGlobal"/> grants global access when the config-level
    /// PermittedGroups contains "*". Development-only convenience; never enable in production.
    /// </summary>
    public AuthorizationService(bool allowWildcardGlobal)
    {
        _allowWildcardGlobal = allowWildcardGlobal;
    }

    public void InitGroups(IConfigProvider config, IGroupResolver grpfinder)
    {
        _config = config;

        foreach (string group in config.PermittedGroups)
        {
            string grpsid = grpfinder.GetSID(group);
            if (!string.IsNullOrEmpty(grpsid) && !_globalGroupSIDs.Contains(grpsid))
            {
                _globalGroupSIDs.Add(grpsid);
            }
        }

        foreach (ConfigCmd cmd in config.Commands)
        {
            var sids = new List<string>();
            foreach (string group in cmd.PermittedGroups)
            {
                string grpsid = grpfinder.GetSID(group);
                if (!string.IsNullOrEmpty(grpsid) && !sids.Contains(grpsid))
                {
                    sids.Add(grpsid);
                }
            }

            _commandGroupSIDs[cmd.ID] = sids;
        }
    }

    public bool IsGlobalUser(IUserContext uinfo)
    {
        if (_allowWildcardGlobal && _config.PermittedGroups.Contains("*"))
        {
            return true;
        }

        foreach (string usersid in uinfo.MemberOfIds)
        {
            if (_globalGroupSIDs.Contains(usersid)) return true;
        }

        return false;
    }

    public bool IsCommandAvailable(IUserContext uinfo, string commandId)
    {
        if (IsGlobalUser(uinfo)) return true;

        if (_commandGroupSIDs.TryGetValue(commandId, out List<string> sids))
        {
            if (sids.Contains("*")) return true;
            foreach (string usersid in uinfo.MemberOfIds)
            {
                if (sids.Contains(usersid)) return true;
            }
        }

        return false;
    }

    public ConfigCmd GetCommand(IUserContext uinfo, string commandId)
    {
        ConfigCmd foundCmd = null;

        foreach (ConfigCmd cmd in _config.Commands)
        {
            if (cmd.ID == commandId)
            {
                foundCmd = cmd;
            }
        }

        if (foundCmd != null)
        {
            if (IsGlobalUser(uinfo) || IsCommandAvailable(uinfo, commandId))
            {
                return foundCmd;
            }
        }

        return null;
    }

    public List<MenuItem> GetMenu(IUserContext uinfo)
    {
        var menuitems = new List<MenuItem>();
        bool globaluser = IsGlobalUser(uinfo);
        dlog.Trace("GetMenu: IsGlobalUser: " + globaluser.ToString());

        dlog.Trace("Building Menu");
        foreach (ConfigCmd cmd in _config.Commands)
        {
            if (IsCommandAvailable(uinfo, cmd.ID) || globaluser)
            {
                menuitems.Add(cmd.GetMenuItem());
            }
        }

        return menuitems;
    }

    [Obsolete("Server-side menu rendering was replaced by the /api/menu endpoint.")]
    public DataTable GetMenuDataTable(IUserContext uinfo, string activeID)
    {
        var dt = new DataTable();
        DataRow dr;

        var dispName = new DataColumn("DisplayName", typeof(string));
        var description = new DataColumn("Description", typeof(string));
        var uri = new DataColumn("Uri", typeof(string));
        var css = new DataColumn("CSS", typeof(string));
        dt.Columns.Add(dispName);
        dt.Columns.Add(description);
        dt.Columns.Add(uri);
        dt.Columns.Add(css);

        List<MenuItem> menu = GetMenu(uinfo);
        foreach (MenuItem mi in menu)
        {
            dr = dt.NewRow();
            dr["DisplayName"] = mi.DisplayName;
            dr["Description"] = mi.Description;
            dr["Uri"] = mi.Uri();
            dr["CSS"] = "";
            if (activeID == mi.ID) dr["CSS"] = "active";
            dt.Rows.Add(dr);
        }

        return dt;
    }
}
