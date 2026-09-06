namespace WebJEA;

public interface ITelemetryService
{
    void Add(string key, object value);
    void Clear(string key);
    void Remove(string key);
    void SendTelemetry();
    void AddIDs(string domainSid, string domainDnsRoot, string scriptId, string userId, bool permitted = true);
    void AddIsOnload(bool state);
    void AddRuntime(float secondsRuntime);
}
