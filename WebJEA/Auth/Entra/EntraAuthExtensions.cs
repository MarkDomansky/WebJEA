using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace WebJEA.Auth.Entra;

public static class EntraAuthExtensions
{
    private static readonly NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Entra ID (Azure AD) authentication: OIDC web-app sign-in with cookies.
    /// Authorization uses the "groups" claim (group object IDs) and the "roles" claim
    /// (app role values). Authentication:Entra:EnableGraph (on by default) adds the
    /// >200-group overage fallback and, with Authentication:Entra:ResolveGroupNamesViaGraph,
    /// display names in PermittedGroups; it needs a client credential, either a certificate
    /// (AzureAd:ClientCredentials) or AzureAd:ClientSecret.
    /// </summary>
    public static WebApplicationBuilder AddWebJeaEntraAuth(this WebApplicationBuilder builder)
    {
        var azureAd = builder.Configuration.GetSection("AzureAd");
        var authBuilder = builder.Services
            .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(azureAd);

        bool enableGraph = builder.Configuration.GetValue("Authentication:Entra:EnableGraph", true);
        if (enableGraph)
        {
            LogGraphCredential(azureAd);

            authBuilder
                .EnableTokenAcquisitionToCallDownstreamApi(new[] { "GroupMember.Read.All" })
                .AddMicrosoftGraph(builder.Configuration.GetSection("MicrosoftGraph"))
                .AddInMemoryTokenCaches();

            builder.Services.AddScoped<GraphGroupLoader>();
        }

        builder.Services.AddScoped<IGroupResolver, EntraGroupResolver>();
        builder.Services.AddScoped<IUserContextFactory, EntraUserContextFactory>();

        return builder;
    }

    /// <summary>
    /// Names the client credential(s) Microsoft.Identity.Web will use from the AzureAd section,
    /// e.g. "certificate (StoreWithDistinguishedName)" or "client secret"; "" when none is set.
    /// Mirrors Microsoft.Identity.Web: AzureAd:ClientCredentials wins, and the legacy
    /// ClientCertificates/ClientSecret keys are used only when it is empty. Never includes values.
    /// </summary>
    public static string DescribeGraphCredential(IConfiguration azureAd)
    {
        var sourceTypes = GetSourceTypes(azureAd.GetSection("ClientCredentials"));
        if (sourceTypes.Count == 0)
        {
            sourceTypes = GetSourceTypes(azureAd.GetSection("ClientCertificates"));
            if (!string.IsNullOrEmpty(azureAd["ClientSecret"])) sourceTypes.Add("ClientSecret");
        }

        return string.Join(", ", sourceTypes.Select(DescribeSourceType));
    }

    private static void LogGraphCredential(IConfiguration azureAd)
    {
        string credential = DescribeGraphCredential(azureAd);
        if (credential == "")
        {
            dlog.Warn("Entra: Authentication:Entra:EnableGraph is true but no client credential is configured " +
                      "(AzureAd:ClientCredentials certificate or AzureAd:ClientSecret). Sign-in and Graph lookups will fail; " +
                      "configure one (see docs/entra-appreg.md) or set EnableGraph to false.");
            return;
        }

        dlog.Info("Entra: Graph client credential = " + credential);

        if (GetSourceTypes(azureAd.GetSection("ClientCredentials")).Count > 0 && !string.IsNullOrEmpty(azureAd["ClientSecret"]))
        {
            dlog.Warn("Entra: both AzureAd:ClientCredentials and AzureAd:ClientSecret are set. ClientCredentials is used; " +
                      "remove AzureAd:ClientSecret to avoid confusion.");
        }
    }

    private static List<string> GetSourceTypes(IConfiguration credentials)
    {
        return credentials.GetChildren()
            .Where(c => c.GetChildren().Any())
            .Select(c => c["SourceType"] ?? "")
            .ToList();
    }

    private static string DescribeSourceType(string sourceType)
    {
        if (sourceType.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase)) return "client secret";
        if (sourceType == "") return "credential with no SourceType";
        if (sourceType.StartsWith("SignedAssertion", StringComparison.OrdinalIgnoreCase)
            || sourceType.Equals("CustomSignedAssertion", StringComparison.OrdinalIgnoreCase)) return sourceType;
        return "certificate (" + sourceType + ")";
    }
}
