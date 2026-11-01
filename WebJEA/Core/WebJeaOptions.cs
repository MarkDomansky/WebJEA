namespace WebJEA;

public class WebJeaOptions
{
    /// <summary>Path to the WebJEA JSON config file (was Web.config applicationSettings "configfile").</summary>
    public string ConfigFile { get; set; }

    /// <summary>Development-only: authenticate every request as the configured DevUser.</summary>
    public bool DevAutoLogin { get; set; }

    /// <summary>Optional path base (e.g. "/hr") for reverse-proxy path routing. Applied via
    /// UsePathBase before all other middleware.</summary>
    public string PathBase { get; set; }
}
