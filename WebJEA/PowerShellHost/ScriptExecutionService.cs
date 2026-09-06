namespace WebJEA;

public class ScriptExecutionService
{
    public IScriptEngine Execute(string script,
                                 Dictionary<string, object> parameters,
                                 bool logParameters,
                                 string webjeaUserName,
                                 string webjeaHostName,
                                 bool verbose = false,
                                 bool pipeToOutString = true)
    {
        IScriptEngine ps = new PSEngine();
        ps.Script = script;
        ps.LogParameters = logParameters;
        ps.Parameters = parameters;
        ps.Verbose = verbose;
        ps.PipeToOutString = pipeToOutString;
        ps.WebJEAUserName = webjeaUserName;
        ps.WebJEAHostName = webjeaHostName;
        ps.Run();
        return ps;
    }
}
