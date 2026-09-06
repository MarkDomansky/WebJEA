using System.Management.Automation;

namespace WebJEA;

public interface IScriptEngine
{
    string Script { get; set; }
    Dictionary<string, object> Parameters { get; set; }
    float Runtime { get; }

    bool LogParameters { get; set; }
    bool Verbose { get; set; }
    bool PipeToOutString { get; set; }
    string WebJEAUserName { get; set; }
    string WebJEAHostName { get; set; }
    bool HasErrors { get; set; }

    Queue<OutputData> GetOutputData();
    List<PSObject> GetOutputObjects();

    void Run();
    void AddParameter(string key, object value);
    void RemoveParameter(string key);
    void UpdateParameter(string key, object value);
    void ClearParameters();
    void ClearOutput();
}
