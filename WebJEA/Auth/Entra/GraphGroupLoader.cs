using Microsoft.Graph;

namespace WebJEA.Auth.Entra;

/// <summary>
/// Microsoft Graph lookups for Entra mode: the transitive group membership fallback for
/// token group overage, and optional group display-name resolution.
/// </summary>
public class GraphGroupLoader
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private readonly GraphServiceClient _graph;

    public GraphGroupLoader(GraphServiceClient graph)
    {
        _graph = graph;
    }

    public virtual List<string> GetTransitiveGroupIds()
    {
        var ids = new List<string>();
        try
        {
            var page = _graph.Me.TransitiveMemberOf.GraphGroup.GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = new[] { "id" };
                cfg.QueryParameters.Top = 999;
            }).GetAwaiter().GetResult();

            while (page != null)
            {
                if (page.Value != null)
                {
                    ids.AddRange(page.Value.Where(g => g.Id != null).Select(g => g.Id));
                }

                if (string.IsNullOrEmpty(page.OdataNextLink)) break;

                page = _graph.Me.TransitiveMemberOf.GraphGroup
                    .WithUrl(page.OdataNextLink)
                    .GetAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            dlog.Error("GraphGroupLoader: transitiveMemberOf query failed: " + ex.Message);
        }

        return ids;
    }

    public string FindGroupIdByDisplayName(string displayName)
    {
        try
        {
            var resp = _graph.Groups.GetAsync(cfg =>
            {
                cfg.QueryParameters.Filter = "displayName eq '" + displayName.Replace("'", "''") + "'";
                cfg.QueryParameters.Select = new[] { "id" };
            }).GetAwaiter().GetResult();

            return resp?.Value?.FirstOrDefault()?.Id ?? "";
        }
        catch (Exception ex)
        {
            dlog.Error("GraphGroupLoader: group lookup for '" + displayName + "' failed: " + ex.Message);
            return "";
        }
    }
}
