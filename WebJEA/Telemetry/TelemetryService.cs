namespace WebJEA.Telemetry;

public class TelemetryService : ITelemetryService
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private Dictionary<string, object> Metrics = new Dictionary<string, object>();
    private readonly TelemetryChannel _channel;
    private readonly bool _isDevelopment;

    public TelemetryService(TelemetryChannel channel, IHostEnvironment environment)
    {
        _channel = channel;
        _isDevelopment = environment.IsDevelopment();
    }

    public void Add(string key, object value)
    {
        if (Metrics.ContainsKey(key))
        {
            Metrics[key] = value; // update
        }
        else
        {
            Metrics.Add(key, value); // add
        }
    }

    public void Clear(string key)
    {
        Metrics.Clear();
    }

    public void Remove(string key)
    {
        if (Metrics.ContainsKey(key))
        {
            Metrics.Remove(key);
        }
    }

    public void SendTelemetry()
    {
        dlog.Trace("SendTelemetry");
        // sends whatever telemetry we have; the hosted service drains the queue in the background
        _channel.Enqueue(new Dictionary<string, object>(Metrics));
    }

    public void AddIDs(string domainSid, string domainDnsRoot, string scriptId, string userId, bool permitted = true)
    {
        string oid = Helpers.StringHash256(domainSid + ";" + domainDnsRoot.ToUpper());
        Add("orgid", _isDevelopment ? "DEV" : oid);
        Add("scriptid", Helpers.StringHash256(oid + ";" + scriptId.ToUpper()));
        Add("userid", Helpers.StringHash256(oid + ";" + userId.ToUpper()));
        Add("permitted", permitted);
    }

    public void AddIsOnload(bool state)
    {
        Add("IsOnload", state);
    }

    public void AddRuntime(float secondsRuntime)
    {
        Add("runtimesec", (Math.Ceiling((decimal)secondsRuntime * 10m) / 10m).ToString()); // round up to 1 decimal
    }
}
