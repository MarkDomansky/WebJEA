using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace WebJEA.Auth.Entra;

public static class EntraAuthExtensions
{
    /// <summary>
    /// Entra ID (Azure AD) authentication: OIDC web-app sign-in with cookies.
    /// Authorization uses the "groups" claim (group object IDs) and the "roles" claim
    /// (app role values); enable Authentication:Entra:EnableGraph for the >200-group
    /// overage fallback and Authentication:Entra:ResolveGroupNamesViaGraph to allow
    /// display names in PermittedGroups.
    /// </summary>
    public static WebApplicationBuilder AddWebJeaEntraAuth(this WebApplicationBuilder builder)
    {
        var authBuilder = builder.Services
            .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

        bool enableGraph = builder.Configuration.GetValue<bool>("Authentication:Entra:EnableGraph");
        if (enableGraph)
        {
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
}
