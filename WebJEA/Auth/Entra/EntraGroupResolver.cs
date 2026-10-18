using System.Text.RegularExpressions;

namespace WebJEA.Auth.Entra;

/// <summary>
/// Entra ID group resolver: PermittedGroups entries that are GUIDs (group or user object
/// IDs) pass through normalized. With ResolveGroupNamesViaGraph enabled (the default), non-GUID entries
/// are first looked up in Graph as group display names, except UPN-shaped entries, which
/// can't be display names, and entries starting with Authentication:Entra:AppRolePrefix,
/// which are app role values by definition. Anything else passes through lowercased so it
/// can match an app role value or UPN in the user's token.
/// </summary>
public class EntraGroupResolver : IGroupResolver
{
    // user@domain.tld with no whitespace; UPNs can't contain spaces, display names usually do
    private static readonly Regex UpnPattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private readonly bool _resolveNames;
    private readonly string _appRolePrefix;
    private readonly IServiceProvider _services;

    public EntraGroupResolver(IConfiguration configuration, IServiceProvider services)
    {
        _resolveNames = configuration.GetValue("Authentication:Entra:ResolveGroupNamesViaGraph", true);
        _appRolePrefix = configuration["Authentication:Entra:AppRolePrefix"] ?? "";
        _services = services;
    }

    private bool IsAppRole(string input)
    {
        return _appRolePrefix.Length > 0 && input.StartsWith(_appRolePrefix, StringComparison.OrdinalIgnoreCase);
    }

    public string GetSID(string input)
    {
        if (input == "*") return "*";

        if (Guid.TryParse(input, out Guid groupId))
        {
            return groupId.ToString(); // normalized lowercase GUID
        }

        if (_resolveNames && !UpnPattern.IsMatch(input) && !IsAppRole(input))
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
