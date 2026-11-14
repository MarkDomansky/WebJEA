using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>
/// Entra mode with app-only bearer tokens signed by a test key. The fixture config grants
/// utwp-info to the WebJEA.Automation app role and utwp-error to a role the app doesn't have.
/// </summary>
public class EntraBearerFactory : EntraUnauthenticatedFactory
{
    public const string TenantId = "eeeeeeee-1111-2222-3333-444444444444";
    public const string ClientId = "ffffffff-1111-2222-3333-444444444444";
    public const string Issuer = "https://login.microsoftonline.com/" + TenantId + "/v2.0";
    public const string V1Issuer = "https://sts.windows.net/" + TenantId + "/";
    public const string CallerAppId = "aaaaaaaa-1111-2222-3333-444444444444";
    public const string AutomationRole = "WebJEA.Automation";

    public static readonly RsaSecurityKey SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };

    public EntraBearerFactory() : base(null, config =>
    {
        foreach (JObject cmd in config["Commands"])
        {
            string id = (string)cmd["Id"];
            if (id == "utwp-info") cmd["PermittedGroups"] = new JArray(AutomationRole);
            if (id == "utwp-error") cmd["PermittedGroups"] = new JArray("Other.Role");
        }
    })
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.IssuerValidator = null;
                options.TokenValidationParameters.ValidIssuers = new[] { Issuer, V1Issuer };
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            }));
    }

    /// <summary>A v2 access token for WebJEA; override or remove claims per test.</summary>
    public static string Token(Action<Dictionary<string, object>> customize = null, DateTime? expires = null, string issuer = Issuer)
    {
        var claims = new Dictionary<string, object>
        {
            ["aud"] = ClientId,
            ["tid"] = TenantId,
            ["oid"] = "bbbbbbbb-1111-2222-3333-444444444444",
            ["sub"] = "bbbbbbbb-1111-2222-3333-444444444444",
            ["azp"] = CallerAppId,
            ["ver"] = "2.0",
            ["roles"] = new[] { AutomationRole }
        };
        customize?.Invoke(claims);

        DateTime exp = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Claims = claims,
            NotBefore = exp.AddHours(-2),
            IssuedAt = exp.AddHours(-2),
            Expires = exp,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256)
        });
    }
}

/// <summary>
/// Same, with Graph and a client credential configured, so execute goes through the
/// sign-in revalidator. Any Graph call would fail (no network), so a passing execute
/// shows app-only callers skip it.
/// </summary>
public class EntraBearerRevalidatingFactory : EntraBearerFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Authentication:Entra:EnableGraph", "true");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
    }
}

public class EntraBearerTests : IClassFixture<EntraBearerFactory>, IClassFixture<EntraBearerRevalidatingFactory>
{
    private readonly EntraBearerFactory _factory;
    private readonly EntraBearerRevalidatingFactory _revalidatingFactory;

    public EntraBearerTests(EntraBearerFactory factory, EntraBearerRevalidatingFactory revalidatingFactory)
    {
        _factory = factory;
        _revalidatingFactory = revalidatingFactory;
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Execute(string cmdid)
    {
        return new StringContent("{\"cmdid\":\"" + cmdid + "\",\"parameters\":{}}", Encoding.UTF8, "application/json");
    }

    private static async Task AssertRejected(HttpResponseMessage response)
    {
        Assert.Equal(401, (int)response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("error").GetString()));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("requestId").GetString()));
    }

    [Fact]
    public async Task Menu_AppTokenWithRole_ReturnsPermittedCommands()
    {
        var response = await Client(_factory, EntraBearerFactory.Token()).GetAsync("/api/menu");

        Assert.Equal(200, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Contains("utwp-info", ids);
        Assert.DoesNotContain("utwp-error", ids);
    }

    [Fact]
    public async Task Config_AppToken_NamesTheCallingApp()
    {
        var response = await Client(_factory, EntraBearerFactory.Token()).GetAsync("/api/config");

        Assert.Equal(200, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("app:" + EntraBearerFactory.CallerAppId, doc.RootElement.GetProperty("userName").GetString());
        Assert.False(doc.RootElement.GetProperty("isGlobalUser").GetBoolean());
    }

    [Fact]
    public async Task Execute_AppTokenWithRole_Runs()
    {
        var response = await Client(_factory, EntraBearerFactory.Token()).PostAsync("/api/execute", Execute("utwp-info"));

        Assert.Equal(200, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("OK", doc.RootElement.GetProperty("statusmessage").GetString());
    }

    [Fact]
    public async Task Execute_AppTokenWithRole_SkipsSignInRevalidation()
    {
        var response = await Client(_revalidatingFactory, EntraBearerFactory.Token()).PostAsync("/api/execute", Execute("utwp-info"));

        Assert.Equal(200, (int)response.StatusCode);
    }

    [Fact]
    public async Task Execute_AppTokenRoleNotPermitted_Returns403()
    {
        var response = await Client(_factory, EntraBearerFactory.Token()).PostAsync("/api/execute", Execute("utwp-error"));

        Assert.Equal(403, (int)response.StatusCode);
    }

    [Fact]
    public async Task Execute_FormPost_Returns415()
    {
        var response = await Client(_factory, EntraBearerFactory.Token()).PostAsync("/api/execute",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["cmdid"] = "utwp-info" }));

        Assert.Equal(415, (int)response.StatusCode);
    }

    [Fact]
    public async Task Api_AppTokenWithoutRoles_Returns401Json()
    {
        string token = EntraBearerFactory.Token(c => c.Remove("roles"));

        await AssertRejected(await Client(_factory, token).GetAsync("/api/menu"));
    }

    [Fact]
    public async Task Api_WrongAudience_Returns401Json()
    {
        string token = EntraBearerFactory.Token(c => c["aud"] = "api://some-other-app");

        await AssertRejected(await Client(_factory, token).GetAsync("/api/menu"));
    }

    [Fact]
    public async Task Config_V1AppToken_IsAcceptedAndNamedFromAppId()
    {
        // App registrations issue v1 tokens unless the manifest asks for v2: the audience is
        // the Application ID URI and the caller is in "appid" rather than "azp"
        string token = EntraBearerFactory.Token(c =>
        {
            c["aud"] = "api://" + EntraBearerFactory.ClientId;
            c["ver"] = "1.0";
            c.Remove("azp");
            c["appid"] = EntraBearerFactory.CallerAppId;
        }, issuer: EntraBearerFactory.V1Issuer);

        var response = await Client(_factory, token).GetAsync("/api/config");

        Assert.Equal(200, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("app:" + EntraBearerFactory.CallerAppId, doc.RootElement.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Api_ExpiredToken_Returns401Json()
    {
        string token = EntraBearerFactory.Token(expires: DateTime.UtcNow.AddHours(-1));

        await AssertRejected(await Client(_factory, token).GetAsync("/api/menu"));
    }

    [Fact]
    public async Task Api_WrongSigningKey_Returns401Json()
    {
        string token = EntraBearerFactory.Token();
        string tampered = token.Substring(0, token.LastIndexOf('.') + 1) + "AAAA";

        await AssertRejected(await Client(_factory, tampered).GetAsync("/api/menu"));
    }

    [Fact]
    public async Task Api_DelegatedToken_Returns401Json()
    {
        string token = EntraBearerFactory.Token(c =>
        {
            c.Remove("roles");
            c["scp"] = "user_impersonation";
            c["preferred_username"] = "alice@contoso.com";
        });

        await AssertRejected(await Client(_factory, token).GetAsync("/api/menu"));
    }

    [Fact]
    public async Task Api_DelegatedTokenWithForgedAppOnlyClaim_Returns401Json()
    {
        // A token can't make itself app-only with a claim named like WebJEA's marker
        string token = EntraBearerFactory.Token(c =>
        {
            c.Remove("roles");
            c["scp"] = "user_impersonation";
            c["webjea_apponly"] = "true";
        });

        await AssertRejected(await Client(_factory, token).GetAsync("/api/menu"));
    }

    [Fact]
    public async Task Page_InvalidBearerToken_Returns401NotRedirect()
    {
        var response = await Client(_factory, "not-a-token").GetAsync("/");

        await AssertRejected(response);
    }
}
