namespace WebJEA.Auth.Entra;

/// <summary>
/// Entra ID group resolver: PermittedGroups entries that are GUIDs (group or user object
/// IDs) pass through normalized. With ResolveGroupNamesViaGraph enabled, non-GUID entries
/// are first looked up in Graph as group display names. Anything else passes through
/// lowercased so it can match an app role value or UPN in the user's token.
/// </summary>
public class EntraGroupResolver : IGroupResolver
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private readonly bool _resolveNames;
    private readonly IServiceProvider _services;

    public EntraGroupResolver(IConfiguration configuration, IServiceProvider services)
    {
        _resolveNames = configuration.GetValue<bool>("Authentication:Entra:ResolveGroupNamesViaGraph");
        _services = services;
    }

    public string GetSID(string input)
    {
        if (input == "*") return "*";

        if (Guid.TryParse(input, out Guid groupId))
        {
            return groupId.ToString(); // normalized lowercase GUID
        }

        if (_resolveNames)
        {
            var loader = _services.GetService<GraphGroupLoader>();
            if (loader != null)
            {
                string id = loader.FindGroupIdByDisplayName(input);
                if (!string.IsNullOrEmpty(id))
                {
                    dlog.Trace("EntraGroupResolver: resolved '" + input + "' to " + id);
                    return id.ToLowerInvariant();
                }
            }
        }

        dlog.Trace("EntraGroupResolver: '" + input +
                   "' is not a GUID; matching it as an app role value or UPN.");
        return input.ToLowerInvariant();
    }
}
