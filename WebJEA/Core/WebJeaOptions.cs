namespace WebJEA;

public class WebJeaOptions
{
    /// <summary>Path to the WebJEA JSON config file (was Web.config applicationSettings "configfile").</summary>
    public string ConfigFile { get; set; }

    /// <summary>Development-only: authenticate every request as the configured DevUser.</summary>
    public bool DevAutoLogin { get; set; }

    /// <summary>Native HTTP-to-HTTPS redirect (replaces the IIS URL Rewrite rule) is
    /// automatic: on whenever HttpPort, HttpsPort, and CertThumbprint are all
    /// configured. There is no separate opt-in setting.</summary>
    public bool ShouldRedirectHttpToHttps =>
        (HttpPort ?? 0) > 0 && (HttpsPort ?? 0) > 0 && !string.IsNullOrWhiteSpace(CertThumbprint);

    /// <summary>Kestrel HTTP listener port (Windows service hosting). Null/0 = no HTTP listener.
    /// When neither HttpPort nor HttpsPort is set, Kestrel uses its default configuration
    /// (ASPNETCORE_URLS etc.) — the container hosting path.</summary>
    public int? HttpPort { get; set; }

    /// <summary>Kestrel HTTPS listener port (Windows service hosting) and the port used when
    /// building the HTTPS redirect location. Null/0 = no HTTPS listener — in that case
    /// ShouldRedirectHttpToHttps is false, so no redirect is attempted.</summary>
    public int? HttpsPort { get; set; }

    /// <summary>Thumbprint of the HTTPS certificate in the LocalMachine\My store. Required
    /// when HttpsPort is set.</summary>
    public string CertThumbprint { get; set; }

    /// <summary>Optional path base (e.g. "/hr") for reverse-proxy path routing. Applied via
    /// UsePathBase before all other middleware.</summary>
    public string PathBase { get; set; }
}
