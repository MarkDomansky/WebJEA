using System.Security.Cryptography.X509Certificates;

namespace WebJEA.Hosting;

/// <summary>
/// Windows-service hosting: turns WebJEA:HttpPort/HttpsPort/CertThumbprint into Kestrel
/// listeners so customers configure WebJEA settings instead of raw Kestrel JSON. When no
/// port is set the app falls through to Kestrel's default configuration (containers use
/// ASPNETCORE_URLS).
/// </summary>
public static class KestrelEndpoints
{
    public static bool IsConfigured(WebJeaOptions options)
    {
        return (options.HttpPort ?? 0) > 0 || (options.HttpsPort ?? 0) > 0;
    }

    /// <summary>Fatal misconfigurations throw.</summary>
    public static void Validate(WebJeaOptions options)
    {
        if (!IsConfigured(options))
        {
            throw new InvalidOperationException(
                "At least one of WebJEA:HttpPort or WebJEA:HttpsPort must be set to a port number.");
        }

        bool httpsEnabled = (options.HttpsPort ?? 0) > 0;

        if (httpsEnabled && string.IsNullOrWhiteSpace(options.CertThumbprint))
        {
            throw new InvalidOperationException(
                $"WebJEA:HttpsPort is {options.HttpsPort} but WebJEA:CertThumbprint is not set. " +
                "Provide the thumbprint of a certificate in the LocalMachine\\My store, or disable HTTPS.");
        }
    }

    public static X509Certificate2 LoadCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Certificate with thumbprint '{thumbprint}' was not found in the LocalMachine\\My store. " +
                "Import the certificate (with its private key) or correct WebJEA:CertThumbprint.");
        }
        return matches[0];
    }
}
