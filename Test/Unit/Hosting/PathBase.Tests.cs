using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJEA.Tests.Hosting;

/// <summary>Same fixture wiring as WebJeaApiFactory, plus WebJEA:PathBase=/hr.</summary>
public class PathBaseFactory : WebApplicationFactory<Program>
{
    public string ConfigPath { get; }

    public PathBaseFactory()
    {
        string testScripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts"));
        var config = JObject.Parse(File.ReadAllText(Path.Combine(testScripts, "config.json")));
        config["basepath"] = testScripts;
        ConfigPath = Path.Combine(Path.GetTempPath(), "webjea-pathbase-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(ConfigPath, config.ToString());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["WebJEA:ConfigFile"] = ConfigPath,
                ["WebJEA:DevAutoLogin"] = "true",
                ["WebJEA:PathBase"] = "/hr",
                ["DevUser:Name"] = @"DEV\tester",
                ["DevUser:Sids:0"] = "Domain Admins"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(ConfigPath); } catch { }
    }
}

public class PathBaseTests : IClassFixture<PathBaseFactory>
{
    private readonly PathBaseFactory _factory;

    public PathBaseTests(PathBaseFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiConfig_IsServed_UnderPathBase()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/hr/api/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StaticIndex_IsServed_UnderPathBase()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        var response = await client.GetAsync("/hr/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("WebJEA", html);
    }
}
