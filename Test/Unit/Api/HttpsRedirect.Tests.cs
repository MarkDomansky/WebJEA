using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>
/// Same fixture setup as <see cref="WebJeaApiFactory"/> but with HttpPort, HttpsPort,
/// and CertThumbprint all set, so WebJeaOptions.ShouldRedirectHttpToHttps is true.
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
                ["WebJEA:HttpPort"] = "8080",
                ["WebJEA:HttpsPort"] = "8443",
                ["WebJEA:CertThumbprint"] = "AB12"
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

    // The redirect-disabled path (default configuration, no ports set at all) is
    // exercised by every test in ApiEndpointsTests: WebJeaApiFactory serves
    // plain-HTTP requests with 200s.
}

/// <summary>
/// HttpPort alone (no HttpsPort/CertThumbprint) is a valid HTTP-only configuration:
/// ShouldRedirectHttpToHttps requires all three settings, so plain HTTP is served
/// normally with no redirect. This is what Deploy.ps1 -AllowHttpWithoutRedirect
/// installs produce.
/// </summary>
public class HttpOnlyNoRedirectFactory : WebJeaApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["WebJEA:HttpPort"] = "8080"
            });
        });
    }
}

public class HttpOnlyNoRedirectTests : IClassFixture<HttpOnlyNoRedirectFactory>
{
    private readonly HttpOnlyNoRedirectFactory _factory;

    public HttpOnlyNoRedirectTests(HttpOnlyNoRedirectFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HttpRequest_WithNoHttpsPortOrCert_IsServedNormally()
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
