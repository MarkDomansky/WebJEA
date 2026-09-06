using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>
/// Hosts the real app (Development environment + DevAutoLogin) against the Test/Scripts
/// fixtures, with the fixture config's basepath rewritten to the local checkout.
/// </summary>
public class WebJeaApiFactory : WebApplicationFactory<Program>
{
    public string ConfigPath { get; }

    public WebJeaApiFactory()
    {
        string testScripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts"));

        var config = JObject.Parse(File.ReadAllText(Path.Combine(testScripts, "config.json")));
        config["basepath"] = testScripts;

        ConfigPath = Path.Combine(Path.GetTempPath(), "webjea-apitest-" + Guid.NewGuid().ToString("N") + ".json");
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
                ["DevUser:Name"] = @"DEV\tester",
                // matches the fixture config's root permittedgroups, making the dev user global
                ["DevUser:Sids:0"] = "Domain Admins"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            File.Delete(ConfigPath);
        }
        catch
        {
        }
    }
}

public class ApiEndpointsTests : IClassFixture<WebJeaApiFactory>
{
    private readonly WebJeaApiFactory _factory;

    public ApiEndpointsTests(WebJeaApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static StringContent JsonBody(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    #region /api/config

    [Fact]
    public async Task Config_ReturnsAppInfo()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/config");

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Config Title", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(@"DEV\tester", doc.RootElement.GetProperty("userName").GetString());
        Assert.True(doc.RootElement.GetProperty("isGlobalUser").GetBoolean());
        Assert.Equal("en-US", doc.RootElement.GetProperty("htmlLanguage").GetString());
        Assert.Contains("WebJEA is now installed", doc.RootElement.GetProperty("dashboardHtml").GetString());
    }

    [Fact]
    public async Task Config_HasSecurityHeaders()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/config");

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    #endregion

    #region /api/menu

    [Fact]
    public async Task Menu_ContainsValidateCommandWithHtmlUri()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/menu");

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var validate = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "validate");
        Assert.Equal("Overview", validate.GetProperty("displayName").GetString());
        Assert.Equal("command.html?cmdid=validate", validate.GetProperty("uri").GetString());
    }

    #endregion

    #region /api/command/{cmdid}

    [Fact]
    public async Task CommandMetadata_Validate_ReturnsFullContract()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/command/validate");

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("validate", root.GetProperty("id").GetString());
        Assert.Equal("Overview", root.GetProperty("displayName").GetString());
        Assert.Equal("Config Title", root.GetProperty("title").GetString());
        Assert.True(root.GetProperty("hasScript").GetBoolean());
        Assert.True(root.GetProperty("hasOnload").GetBoolean());
        Assert.True(root.GetProperty("showVerbose").GetBoolean());

        var parameters = root.GetProperty("parameters").EnumerateArray().ToList();
        Assert.NotEmpty(parameters);

        var p01 = parameters.Single(p => p.GetProperty("name").GetString() == "Input01Mandatory");
        Assert.Equal("text", p01.GetProperty("control").GetString());
        Assert.True(p01.GetProperty("isMandatory").GetBoolean());
        Assert.False(string.IsNullOrEmpty(p01.GetProperty("labelOverride").GetString()));

        var lInput2 = parameters.Single(p => p.GetProperty("name").GetString() == "LInput2");
        Assert.Equal("multiselect", lInput2.GetProperty("control").GetString());

        var dInput02 = parameters.Single(p => p.GetProperty("name").GetString() == "DInput02DT");
        Assert.Equal("datetime", dInput02.GetProperty("control").GetString());

        var sw = parameters.Single(p => p.GetProperty("name").GetString() == "Input11Switch");
        Assert.Equal("checkbox", sw.GetProperty("control").GetString());
    }

    [Fact]
    public async Task CommandMetadata_UnknownCommand_Returns403()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/command/nonexistent");

        Assert.Equal(403, (int)response.StatusCode);
    }

    #endregion

    #region /api/execute

    [Fact]
    public async Task Execute_Get_Returns405Json()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/execute");

        Assert.Equal(405, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(405, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Execute_WrongContentType_Returns415()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", new StringContent("cmdid=x", Encoding.UTF8, "text/plain"));

        Assert.Equal(415, (int)response.StatusCode);
    }

    [Fact]
    public async Task Execute_InvalidJson_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", JsonBody("{not json"));

        Assert.Equal(400, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid JSON in request body.", doc.RootElement.GetProperty("statusmessage").GetString());
    }

    [Fact]
    public async Task Execute_BadCmdidFormat_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", JsonBody("{\"cmdid\":\"bad cmd id!\"}"));

        Assert.Equal(400, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid cmdid format.", doc.RootElement.GetProperty("statusmessage").GetString());
    }

    [Fact]
    public async Task Execute_UnknownCommand_Returns403()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", JsonBody("{\"cmdid\":\"nonexistent\"}"));

        Assert.Equal(403, (int)response.StatusCode);
    }

    [Fact]
    public async Task Execute_MissingMandatoryParameters_Returns400WithErrorMessages()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", JsonBody("{\"cmdid\":\"validate\",\"parameters\":{}}"));

        Assert.Equal(400, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Parameter validation failed.", doc.RootElement.GetProperty("statusmessage").GetString());
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal("error", m.GetProperty("stream").GetString()));
    }

    [Fact]
    public async Task Execute_InfoScript_Returns200WithInfoStream()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", JsonBody("{\"cmdid\":\"utwp-info\",\"parameters\":{}}"));

        Assert.Equal(200, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(200, root.GetProperty("status").GetInt32());
        Assert.Equal("OK", root.GetProperty("statusmessage").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("output").ValueKind);

        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        var info = messages.Single(m => m.GetProperty("stream").GetString() == "info");
        Assert.Contains("information message", info.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Execute_ErrorScript_Returns200WithErrStream()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/execute", JsonBody("{\"cmdid\":\"utwp-error\",\"parameters\":{}}"));

        Assert.Equal(200, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed with errors in stream.", doc.RootElement.GetProperty("statusmessage").GetString());
        Assert.Contains(doc.RootElement.GetProperty("messages").EnumerateArray(),
            m => m.GetProperty("stream").GetString() == "err");
    }

    #endregion

    #region legacy redirects

    [Fact]
    public async Task DefaultAspx_RedirectsToRoot()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/default.aspx");

        Assert.Equal(301, (int)response.StatusCode);
        Assert.Equal("/", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task DefaultAspx_WithCmdid_RedirectsToCommandHtml()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/default.aspx?cmdid=validate&Input01Mandatory=x");

        Assert.Equal(301, (int)response.StatusCode);
        Assert.Equal("/command.html?cmdid=validate&Input01Mandatory=x", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task CommandAspx_RedirectsToCommandHtml()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/command.aspx?cmdid=validate");

        Assert.Equal(301, (int)response.StatusCode);
        Assert.Equal("/command.html?cmdid=validate", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task WebjeaLegacyUrl_RedirectsInOneHop()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/webjea/default.aspx?cmdid=validate");

        Assert.Equal(301, (int)response.StatusCode);
        Assert.Equal("/command.html?cmdid=validate", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task PostToLegacyPageUrl_Returns405()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/webjea/default.aspx?cmdid=validate",
            new StringContent("Input01Mandatory=x", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(405, (int)response.StatusCode);
        Assert.Equal(new[] { "GET", "HEAD" }, response.Content.Headers.Allow.OrderBy(m => m));
    }

    #endregion
}
