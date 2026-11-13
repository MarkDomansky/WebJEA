using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>
/// Entra mode with no signed-in user. The OIDC metadata is supplied up front so a
/// challenge never reaches out to login.microsoftonline.com.
/// </summary>
public class EntraUnauthenticatedFactory : WebJeaApiFactory
{
    public const string AuthorizeEndpoint = "https://login.example.test/authorize";

    private readonly string _pathBase;

    public EntraUnauthenticatedFactory() : this(null)
    {
    }

    protected EntraUnauthenticatedFactory(string pathBase) : base(null, "Domain Admins")
    {
        _pathBase = pathBase;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Program reads the auth mode before the host is built, where only host settings
        // (not ConfigureAppConfiguration) are visible
        builder.UseSetting("WebJEA:DevAutoLogin", "false");
        builder.UseSetting("Authentication:Mode", "Entra");
        builder.UseSetting("Authentication:Entra:EnableGraph", "false");
        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId", "eeeeeeee-1111-2222-3333-444444444444");
        builder.UseSetting("AzureAd:ClientId", "ffffffff-1111-2222-3333-444444444444");
        builder.UseSetting("AzureAd:CallbackPath", "/signin-oidc");
        if (_pathBase != null) builder.UseSetting("WebJEA:PathBase", _pathBase);

        builder.ConfigureTestServices(services =>
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = AuthorizeEndpoint };
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
            }));
    }
}

public class EntraUnauthenticatedPathBaseFactory : EntraUnauthenticatedFactory
{
    public EntraUnauthenticatedPathBaseFactory() : base("/hr")
    {
    }
}

public class EntraApiChallengeTests : IClassFixture<EntraUnauthenticatedFactory>, IClassFixture<EntraUnauthenticatedPathBaseFactory>
{
    private readonly EntraUnauthenticatedFactory _factory;
    private readonly EntraUnauthenticatedPathBaseFactory _pathBaseFactory;

    public EntraApiChallengeTests(EntraUnauthenticatedFactory factory, EntraUnauthenticatedPathBaseFactory pathBaseFactory)
    {
        _factory = factory;
        _pathBaseFactory = pathBaseFactory;
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("/api/menu")]
    [InlineData("/api/config")]
    [InlineData("/api/command/validate")]
    public async Task Api_Unauthenticated_Returns401Json(string url)
    {
        var response = await Client(_factory).GetAsync(url);

        Assert.Equal(401, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Authentication required.", doc.RootElement.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("requestId").GetString()));
    }

    [Fact]
    public async Task Execute_Unauthenticated_Returns401()
    {
        var response = await Client(_factory).PostAsync("/api/execute",
            new StringContent("{\"cmdid\":\"validate\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Page_Unauthenticated_StillRedirectsToSignIn()
    {
        var response = await Client(_factory).GetAsync("/");

        Assert.Equal(302, (int)response.StatusCode);
        Assert.StartsWith(EntraUnauthenticatedFactory.AuthorizeEndpoint, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Api_UnderPathBase_Returns401()
    {
        var response = await Client(_pathBaseFactory).GetAsync("/hr/api/menu");

        Assert.Equal(401, (int)response.StatusCode);
    }
}
