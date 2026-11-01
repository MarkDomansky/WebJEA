using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>
/// Same fixture setup as <see cref="WebJeaApiFactory"/> with HTTPS_PORT set. TestServer has
/// no bound addresses, so this is the redirect middleware's config fallback; a real
/// Windows-service install gets the port from its bound Kestrel:Endpoints:Https listener.
/// </summary>
public class HttpsRedirectFactory : WebJeaApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["HTTPS_PORT"] = "8443"
            });
        });
    }
}

public class HttpsRedirectTests : IClassFixture<HttpsRedirectFactory>
{
    private readonly HttpsRedirectFactory _factory;

    public HttpsRedirectTests(HttpsRedirectFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HttpRequest_IsPermanentlyRedirectedToHttps()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

        var response = await client.GetAsync("/api/config?x=1");

        Assert.Equal(System.Net.HttpStatusCode.PermanentRedirect, response.StatusCode); // 308
        Assert.Equal("https://localhost:8443/api/config?x=1", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task HttpsRequest_IsServedNormally()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/config");

        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// No HTTPS listener and no HTTPS_PORT: the redirect middleware cannot determine a port, so
/// plain HTTP is served normally. This is what Deploy.ps1 -AllowHttpWithoutRedirect installs
/// (and containers behind a TLS-terminating proxy) look like. The base WebJeaApiFactory is
/// already exactly this configuration.
/// </summary>
public class HttpOnlyNoRedirectTests : IClassFixture<WebJeaApiFactory>
{
    private readonly WebJeaApiFactory _factory;

    public HttpOnlyNoRedirectTests(WebJeaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HttpRequest_WithNoHttpsPort_IsServedNormally()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

        var response = await client.GetAsync("/api/config");

        response.EnsureSuccessStatusCode();
    }
}

/// <summary>An appsettings file from an earlier alpha (WebJEA:HttpPort etc.) must stop startup.</summary>
public class LegacyEndpointSettingsFactory : WebJeaApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["WebJEA:HttpsPort"] = "443"
            });
        });
    }
}

public class LegacyEndpointSettingsTests
{
    [Fact]
    public void Startup_Fails_WhenLegacyPortSettingPresent()
    {
        using var factory = new LegacyEndpointSettingsFactory();
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("WebJEA:HttpsPort", ex.ToString());
    }
}
