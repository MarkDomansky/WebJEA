namespace WebJEA;

public class Config : IConfigProvider
{
    public string Title { get; set; } = "WebJEA";
    public bool LogParameters { get; set; } = true;
    public List<ConfigCmd> Commands { get; set; }
    public string BasePath { get; set; }
    public bool SendTelemetry { get; set; } = true;
    public string HtmlLanguage { get; set; } = "en-US";
    public string DashboardHtml { get; set; }

    /// <summary>
    /// HTML that follows the "not available" message (empty menu, unknown or denied
    /// command). Unset means the client's default "Contact your WebJEA administrator..." line.
    /// </summary>
    public string NoAccessHelpHtml { get; set; }
    public bool ShowVerbose { get; set; } = true;
    [Newtonsoft.Json.JsonConverter(typeof(SingleOrListConverter))]
    public List<string> PermittedGroups { get; set; } = new List<string>();

    /// <summary>Accepted for schema compatibility (Legacy|Markdown|Link); no renderer branches on it yet.</summary>
    public string RenderMode { get; set; } = "Legacy";
}
