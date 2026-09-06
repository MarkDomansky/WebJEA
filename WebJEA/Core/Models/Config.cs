namespace WebJEA;

public class Config : IConfigProvider
{
    public string Title { get; set; }
    public bool LogParameters { get; set; } = true;
    public List<ConfigCmd> Commands { get; set; }
    public string BasePath { get; set; }
    public bool SendTelemetry { get; set; } = true;
    public string HtmlLanguage { get; set; } = "en-US";
    public string DashboardHtml { get; set; }
    public bool ShowVerbose { get; set; } = true;
    public List<string> PermittedGroups { get; set; } = new List<string>();

    /// <summary>Accepted for schema compatibility (Legacy|Markdown); no renderer branches on it yet.</summary>
    public string RenderMode { get; set; } = "Legacy";
}
