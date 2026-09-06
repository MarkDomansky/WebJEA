namespace WebJEA;

public class ConfigCmd
{
    private string prvID;

    public string DisplayName { get; set; }

    /// <summary>Null means "use the config-level default" (the old TriState.UseDefault).</summary>
    public bool? LogParameters { get; set; }

    public string Synopsis { get; set; }
    public string Description { get; set; }
    public string OnloadScript { get; set; }
    public string Script { get; set; }
    public List<string> PermittedGroups { get; set; } = new List<string>();

    /// <summary>Accepted for schema compatibility (Legacy|Markdown); no renderer branches on it yet.</summary>
    public string RenderMode { get; set; }

    public string ID
    {
        get => prvID;
        set => prvID = value.ToLower();
    }

    public MenuItem GetMenuItem()
    {
        var mi = new MenuItem();
        mi.ID = ID;
        mi.DisplayName = DisplayName ?? ID;
        mi.Description = Description;
        mi.Synopsis = Synopsis;
        return mi;
    }
}
