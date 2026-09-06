namespace WebJEA;

public interface IConfigProvider
{
    string Title { get; set; }
    bool LogParameters { get; set; }
    List<ConfigCmd> Commands { get; }
    string BasePath { get; set; }
    bool SendTelemetry { get; set; }
    string HtmlLanguage { get; set; }
    string DashboardHtml { get; set; }
    bool ShowVerbose { get; set; }
    List<string> PermittedGroups { get; set; }
    string RenderMode { get; set; }
}
