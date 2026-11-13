using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using WebJEA.Api;

namespace WebJEA.Auth.Entra;

public static class EntraAuthExtensions
{
    private static readonly NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Entra ID (Azure AD) authentication: OIDC web-app sign-in with cookies.
    /// Authorization uses the "groups" claim (group object IDs) and the "roles" claim
    /// (app role values). Authentication:Entra:EnableGraph (on by default) adds the
    /// >200-group overage fallback, display names in PermittedGroups (with
    /// Authentication:Entra:ResolveGroupNamesViaGraph), and, when a client credential
    /// (AzureAd:ClientCredentials certificate or AzureAd:ClientSecret) is configured,
    /// near-real-time revalidation of the user before each script execution (CAE).
    /// Unauthenticated /api requests get a 401 instead of a redirect to sign-in.
    /// </summary>
    public static WebApplicationBuilder AddWebJeaEntraAuth(this WebApplicationBuilder builder)
    {
        var azureAd = builder.Configuration.GetSection("AzureAd");
        bool enableGraph = builder.Configuration.GetValue("Authentication:Entra:EnableGraph", true);
        bool revalidate = enableGraph && DescribeGraphCredential(azureAd) != "";

        // Declare the CAE client capability before Microsoft.Identity.Web binds the section,
        // so Graph issues CAE tokens it revokes on critical events (disable, revoke sessions).
        if (revalidate)
        {
            AddClientCapability(builder.Configuration, azureAd, "cp1");
        }

        var authBuilder = builder.Services
            .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(azureAd);

        builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, ReturnUnauthorizedForApi);

        if (enableGraph)
        {
            LogGraphCredential(azureAd);

            authBuilder
                .EnableTokenAcquisitionToCallDownstreamApi(new[] { "GroupMember.Read.All" })
                .AddMicrosoftGraph(builder.Configuration.GetSection("MicrosoftGraph"))
                .AddInMemoryTokenCaches();

            builder.Services.AddScoped<GraphGroupLoader>();
        }

        if (revalidate)
        {
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<RevalidationCache>();
            builder.Services.AddScoped<IUserRevalidator, EntraRevalidator>();

            int cacheSeconds = Math.Max(0, builder.Configuration.GetValue("Authentication:Entra:RevalidateCacheSeconds", RevalidationCache.DefaultCacheSeconds));
            bool failOpen = builder.Configuration.GetValue("Authentication:Entra:RevalidateFailOpen", false);
            dlog.Info("Entra: sign-in revalidation before execute on (cache " + cacheSeconds + "s, " +
                      (failOpen ? "fail-open" : "fail-closed") + "); CAE client capability cp1 declared.");
        }
        else
        {
            dlog.Warn("Entra: sign-in revalidation before execute is off (" +
                      (enableGraph ? "no client credential is configured" : "Authentication:Entra:EnableGraph is false") +
                      "); disabled or revoked users can run scripts until their session expires.");
        }

        builder.Services.AddScoped<IGroupResolver, EntraGroupResolver>();
        builder.Services.AddScoped<IUserContextFactory, EntraUserContextFactory>();

        return builder;
    }

    /// <summary>
    /// API calls can't follow a redirect to the Entra sign-in page, so an unauthenticated
    /// /api request gets a 401 (the SPA offers a reload, which signs in). Pages still redirect.
    /// </summary>
    private static void ReturnUnauthorizedForApi(OpenIdConnectOptions options)
    {
        var redirectToIdentityProvider = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.HandleResponse();
                await EndpointHelpers.WriteErrorAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Authentication required.");
                return;
            }

            await redirectToIdentityProvider(context);
        };
    }

    /// <summary>
    /// Appends a value to AzureAd:ClientCapabilities unless it's already there. Added as a
    /// configuration source so every options type Microsoft.Identity.Web binds from the
    /// section, including token acquisition's, sees it.
    /// </summary>
    internal static void AddClientCapability(IConfigurationBuilder configBuilder, IConfigurationSection azureAd, string capability)
    {
        var existing = azureAd.GetSection("ClientCapabilities").GetChildren().ToList();
        if (existing.Any(c => string.Equals(c.Value, capability, StringComparison.OrdinalIgnoreCase))) return;

        int next = existing.Select(c => int.TryParse(c.Key, out int i) ? i + 1 : 0).DefaultIfEmpty(0).Max();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string>
        {
            [azureAd.Path + ":ClientCapabilities:" + next] = capability
        });
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
