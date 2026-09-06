using Newtonsoft.Json;

namespace WebJEA;

public class CommandService
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();
    private IConfigProvider _config;
    private IAuthorizationService _auth;
    private Dictionary<string, PSCmd> _scriptCmds = new Dictionary<string, PSCmd>();
    private Dictionary<string, PSCmd> _onloadCmds = new Dictionary<string, PSCmd>();

    public CommandService() : this(new AuthorizationService())
    {
    }

    public CommandService(IAuthorizationService auth)
    {
        _auth = auth;
    }

    public IConfigProvider Config => _config;

    public IAuthorizationService Auth => _auth;

    public void LoadConfig(string configFilePath, IGroupResolver grpfinder)
    {
        string configstr = Helpers.GetFileContent(configFilePath);
        try
        {
            _config = JsonConvert.DeserializeObject<Config>(configstr);
        }
        catch (Exception ex)
        {
            throw new Exception("Could not read config file", ex);
        }

        try
        {
            _auth.InitGroups(_config, grpfinder);
        }
        catch (Exception ex)
        {
            throw new Exception("Could not initialize groups", ex);
        }
    }

    public string ResolveCommandId(IUserContext uinfo, string requestedId)
    {
        if (_auth.IsCommandAvailable(uinfo, requestedId))
        {
            return requestedId;
        }

        dlog.Warn("User " + uinfo.UserName + " requested page they don't have access to " + requestedId + ". Showing dashboard.");
        return "";
    }

    public ConfigCmd GetCommand(IUserContext uinfo, string cmdid)
    {
        return _auth.GetCommand(uinfo, cmdid);
    }

    public PSCmd GetScriptCmd(string cmdid)
    {
        if (_scriptCmds.ContainsKey(cmdid))
        {
            return _scriptCmds[cmdid];
        }

        ConfigCmd configCmd = null;
        foreach (ConfigCmd cmd in _config.Commands)
        {
            if (cmd.ID == cmdid)
            {
                configCmd = cmd;
                break;
            }
        }

        if (configCmd == null || string.IsNullOrEmpty(configCmd.Script))
        {
            return null;
        }

        var pscmd = new PSCmd();
        pscmd.Script = configCmd.Script;
        pscmd.LogParameters = configCmd.LogParameters;
        pscmd.Init(_config.BasePath, _config.LogParameters);

        if (string.IsNullOrEmpty(configCmd.Synopsis))
        {
            configCmd.Synopsis = pscmd.ParsedSynopsis;
        }

        if (string.IsNullOrEmpty(configCmd.Description))
        {
            configCmd.Description = pscmd.ParsedDescription;
        }

        _scriptCmds[cmdid] = pscmd;
        return pscmd;
    }

    public PSCmd GetOnloadCmd(string cmdid)
    {
        if (_onloadCmds.ContainsKey(cmdid))
        {
            return _onloadCmds[cmdid];
        }

        ConfigCmd configCmd = null;
        foreach (ConfigCmd cmd in _config.Commands)
        {
            if (cmd.ID == cmdid)
            {
                configCmd = cmd;
                break;
            }
        }

        if (configCmd == null || string.IsNullOrEmpty(configCmd.OnloadScript))
        {
            return null;
        }

        var pscmd = new PSCmd();
        pscmd.Script = configCmd.OnloadScript;
        pscmd.LogParameters = configCmd.LogParameters;
        pscmd.Init(_config.BasePath, _config.LogParameters);

        _onloadCmds[cmdid] = pscmd;
        return pscmd;
    }
}
