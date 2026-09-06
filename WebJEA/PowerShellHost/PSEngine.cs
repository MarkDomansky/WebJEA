using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text;

namespace WebJEA;

public class PSEngine : IScriptEngine
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private PowerShell ps;

    public bool LogParameters { get; set; } = true;
    public bool Verbose { get; set; } = false;
    public bool PipeToOutString { get; set; } = true;
    public string WebJEAUserName { get; set; } = "";
    public string WebJEAHostName { get; set; } = "";

    private string prvScript;
    private Dictionary<string, object> prvParams = new Dictionary<string, object>();
    private float prvRuntime = 0;
    private Queue<OutputData> prvOutputQ = new Queue<OutputData>();
    private List<PSObject> prvOutputObjects = new List<PSObject>();

    public bool HasErrors { get; set; } = false;

    public float Runtime => prvRuntime;

    public string Script
    {
        get => prvScript;
        set => prvScript = value;
    }

    public Dictionary<string, object> Parameters
    {
        get => prvParams;
        set => prvParams = value;
    }

    public Queue<OutputData> GetOutputData()
    {
        return prvOutputQ;
    }

    public List<PSObject> GetOutputObjects()
    {
        return prvOutputObjects;
    }

    public void Run()
    {
        if (!File.Exists(prvScript))
        {
            // file didn't exist
            Enqueue(OutputType.Err, "File Does not Exist");
            return;
        }

        ps = PowerShell.Create();

        // add stream handlers; each PSDataCollection is typed, so each stream gets its own handler
        ps.Streams.Information.DataAdded += (sender, e) =>
        {
            var rec = ((PSDataCollection<InformationRecord>)sender)[e.Index];
            Enqueue(OutputType.Info, rec.ToString());
        };
        ps.Streams.Warning.DataAdded += (sender, e) =>
        {
            var rec = ((PSDataCollection<WarningRecord>)sender)[e.Index];
            Enqueue(OutputType.Warn, rec.ToString());
        };
        ps.Streams.Error.DataAdded += (sender, e) =>
        {
            var rec = ((PSDataCollection<ErrorRecord>)sender)[e.Index];
            string errorMessage = rec.Exception.Message + "\r\n" + rec.ScriptStackTrace + "\r\n    + CategoryInfo          : " + rec.CategoryInfo;
            dlog.Error("PowerShell Error: " + errorMessage);
            Enqueue(OutputType.Err, rec.Exception.Message); //We only output a simplified error message to the user, but log the full error for debugging purposes
            HasErrors = true;
        };
        ps.Streams.Verbose.DataAdded += (sender, e) =>
        {
            var rec = ((PSDataCollection<VerboseRecord>)sender)[e.Index];
            Enqueue(OutputType.Verbose, rec.ToString());
        };
        ps.Streams.Debug.DataAdded += (sender, e) =>
        {
            var rec = ((PSDataCollection<DebugRecord>)sender)[e.Index];
            Enqueue(OutputType.Debug, rec.ToString());
        };

        // Set VerbosePreference if verbose mode is enabled
        if (Verbose)
        {
            ps.Runspace.SessionStateProxy.SetVariable("VerbosePreference", "Continue");
        }

        // Set WebJEA environment variables for script context
        ps.Runspace.SessionStateProxy.SetVariable("env:WebJEAUserName", WebJEAUserName);
        ps.Runspace.SessionStateProxy.SetVariable("env:WebJEAHostName", WebJEAHostName);

        // Set execution policy to bypass for this runspace (execution policies only exist on Windows)
        // FUTURE: Make this configurable via config.json
        if (OperatingSystem.IsWindows())
        {
            ps.AddScript("Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force").Invoke();
            ps.Commands.Clear();
        }

        ps.Commands.AddCommand(prvScript);

        if (prvParams != null)
        {
            foreach (var pair in prvParams)
            {
                ps.Commands.AddParameter(pair.Key, pair.Value);
            }
        }

        if (PipeToOutString)
        {
            ps.Commands.AddCommand("out-string");
        }

        LogCommandExecuted();
        DateTime timestart = DateTime.Now;
        Collection<PSObject> results = new Collection<PSObject>();
        try
        {
            // record the timespan
            results = ps.Invoke();
        }
        catch (ParameterBindingException ex)
        {
            Enqueue(OutputType.Err, ex.ErrorRecord.FullyQualifiedErrorId + ": " + ex.ErrorRecord.Exception.Message + " - " + ex.ErrorRecord.ScriptStackTrace);
        }
        catch (Exception ex)
        {
            // TODO: improve error output, try to emulate actual PS output
            dlog.Error("Error when executing script: " + ex.Message);
            Enqueue(OutputType.Err, ex.Message);
        }

        TimeSpan timespan = DateTime.Now - timestart;
        prvRuntime = (float)timespan.TotalSeconds;
        dlog.Info("Executed|" + prvScript + "|" + prvRuntime);

        if (results.Count > 0)
        {
            if (PipeToOutString)
            {
                foreach (PSObject resobj in results)
                {
                    string content = resobj.ToString();
                    if (content != "")
                    {
                        Enqueue(OutputType.Output, content);
                    }
                }
            }
            else
            {
                foreach (PSObject resobj in results)
                {
                    prvOutputObjects.Add(resobj);
                }
            }
        }

        ps = null;
    }

    private void Enqueue(OutputType outputType, string content)
    {
        var item = new OutputData
        {
            OutputType = outputType,
            Content = content
        };
        prvOutputQ.Enqueue(item);
    }

    private void LogCommandExecuted()
    {
        var strbld = new StringBuilder(prvScript);
        if (LogParameters)
        {
            foreach (var pair in prvParams)
            {
                strbld.Append(" -" + pair.Key);
                if (pair.Value is string[] values)
                {
                    strbld.Append(" @(");
                    foreach (var item in values)
                    {
                        strbld.Append(" '" + item + "',");
                    }

                    strbld.Remove(strbld.Length - 1, 1);
                    strbld.Append(")");
                }
                else
                {
                    strbld.Append(" '" + pair.Value + "'");
                }
            }
        }

        dlog.Info("Executing|" + strbld.ToString() + "|-");
    }

    public void AddParameter(string key, object value)
    {
        UpdateParameter(key, value);
    }

    public void RemoveParameter(string key)
    {
        if (prvParams.ContainsKey(key))
        {
            prvParams.Remove(key);
        }
    }

    public void UpdateParameter(string key, object value)
    {
        RemoveParameter(key);
        prvParams.Add(key, value);
    }

    public void ClearParameters()
    {
        prvParams.Clear();
    }

    public void ClearOutput()
    {
    }
}
