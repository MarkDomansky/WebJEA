namespace WebJEA.Hosting;

/// <summary>
/// Listeners and the HTTPS certificate come from Kestrel's own configuration
/// (Kestrel:Endpoints, which Deploy.ps1 writes for Windows-service installs; ASPNETCORE_URLS
/// and Kestrel:Certificates in containers). Earlier alphas used WebJEA:HttpPort/HttpsPort/
/// CertThumbprint instead; those keys are no longer read, so refuse to start rather than
/// silently fall back to Kestrel's default localhost:5000 listener.
/// </summary>
public static class KestrelEndpoints
{
    internal static readonly string[] LegacyKeys = ["WebJEA:HttpPort", "WebJEA:HttpsPort", "WebJEA:CertThumbprint"];

    public static void RejectLegacySettings(IConfiguration configuration)
    {
        var found = LegacyKeys.Where(key => configuration[key] is not null).ToArray();
        if (found.Length > 0)
        {
            throw new InvalidOperationException(
                $"Retired setting(s) found: {string.Join(", ", found)}. Listener ports and the HTTPS certificate " +
                "now use Kestrel's native Kestrel:Endpoints configuration. Re-run Deploy.ps1 to migrate " +
                "appsettings.Production.json, or remove these keys and configure Kestrel:Endpoints directly. " +
                "See docs/deployment-migration.md.");
        }
    }
}
