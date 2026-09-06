namespace WebJEA;

public class PSCmd
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public string Script { get; set; }

    /// <summary>Null means "use the config-level default" (the old TriState.UseDefault).</summary>
    public bool? LogParameters { get; set; }

    public List<PSCmdParam> Parameters { get; set; } = new List<PSCmdParam>();

    public string ParsedSynopsis { get; private set; } = "";
    public string ParsedDescription { get; private set; } = "";

    public void Init(string basePath, bool defaultLogParams)
    {
        dlog.Debug("PSCmd|Init");

        if (LogParameters == null)
        {
            LogParameters = defaultLogParams;
        }

        if (!string.IsNullOrEmpty(Script))
        {
            Script = RebuildPath(Script, basePath);

            var scriptparser = new PSScriptParser(Script);
            ParsedSynopsis = scriptparser.Synopsis;
            ParsedDescription = scriptparser.Description;

            List<PSCmdParam> newParams = scriptparser.GetParameters();

            // merge existing parameters into newparams
            foreach (PSCmdParam parsedParam in newParams)
            {
                PSCmdParam findMatchedParam = null;
                // this looks for an existing parameter to merge with
                if (Parameters != null)
                {
                    foreach (PSCmdParam parentParam in Parameters)
                    {
                        if (parsedParam.Name.ToUpper() == parentParam.Name.ToUpper())
                        {
                            findMatchedParam = parentParam;
                        }
                    }
                }

                if (findMatchedParam != null)
                {
                    // matched an existing parameter, prefer settings from Parameters over newParams
                    // TODO: remove this logic because we're going to stop honoring parameters stored in the config
                    parsedParam.MergeOver(findMatchedParam);
                }
            }

            // now check for params missing from newParams that are in Parameters
            if (Parameters != null)
            {
                foreach (PSCmdParam parentParam in Parameters)
                {
                    PSCmdParam findMatchedParam = null;
                    foreach (PSCmdParam parsedParam in newParams)
                    {
                        if (parsedParam.Name.ToUpper() == parentParam.Name.ToUpper())
                        {
                            findMatchedParam = parentParam;
                        }
                    }

                    if (findMatchedParam == null)
                    {
                        // did not find a match, add as new
                        newParams.Add(parentParam);
                    }
                }
            }

            // then replace parameters with newParams
            Parameters = newParams;
        }
    }

    private string RebuildPath(string scriptPath, string basePath)
    {
        if (!string.IsNullOrEmpty(scriptPath))
        {
            if (scriptPath != Path.GetFullPath(scriptPath))
            {
                if (Path.IsPathRooted(scriptPath))
                {
                    scriptPath = basePath + scriptPath;
                }
                else
                {
                    scriptPath = basePath + "\\" + scriptPath;
                }
            }
        }

        return scriptPath;
    }
}
