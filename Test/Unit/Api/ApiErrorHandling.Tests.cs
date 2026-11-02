using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>
/// Captures WebJEA's NLog output in memory so tests can assert the reason the log
/// records when the user only sees the generic "not available" message.
/// </summary>
internal static class LogCapture
{
    private static readonly object Gate = new object();
    private static readonly NLog.Targets.MemoryTarget Target = new NLog.Targets.MemoryTarget("apitest-memory")
    {
        // same request ID renderer as NLog.config, so tests can match a response's requestId
        Layout = "${level}|${logger}|${message}|${aspnet-TraceIdentifier}",
        MaxLogsCount = 5000
    };

    /// <summary>Call after the host has started; re-attaches if the host reloaded the NLog config.</summary>
    public static void Ensure()
    {
        lock (Gate)
        {
            var config = NLog.LogManager.Configuration ?? new NLog.Config.LoggingConfiguration();
            if (config.FindTargetByName("apitest-memory") != null) return;

            config.AddRuleForAllLevels(Target);
            NLog.LogManager.Configuration = config;
        }
    }

    public static bool Contains(string text)
    {
        lock (Gate)
        {
            return Target.Logs.ToArray().Any(line => line.Contains(text));
        }
    }

    public static bool Matches(string pattern)
    {
        lock (Gate)
        {
            return Target.Logs.ToArray().Any(line => Regex.IsMatch(line, pattern));
        }
    }
}

internal static class FixtureConfig
{
    public static void RestrictCommands(JObject config, string group, params string[] ids)
    {
        foreach (JObject cmd in (JArray)config["Commands"])
        {
            if (ids.Length == 0 || ids.Contains((string)cmd["Id"]))
            {
                cmd["PermittedGroups"] = new JArray(group);
            }
        }
    }
}

/// <summary>Non-global dev user; "validate" is limited to a group the user isn't in.</summary>
public class RestrictedCommandApiFactory : WebJeaApiFactory
{
    public RestrictedCommandApiFactory() : base(config => FixtureConfig.RestrictCommands(config, "Helpdesk", "validate"), "Nobody")
    {
    }
}

/// <summary>Non-global dev user who is permitted on none of the configured commands.</summary>
public class NoPermittedCommandsApiFactory : WebJeaApiFactory
{
    public NoPermittedCommandsApiFactory() : base(config => FixtureConfig.RestrictCommands(config, "Helpdesk"), "Nobody")
    {
    }
}

/// <summary>Global dev user, "Commands": [] and a custom NoAccessHelpHtml.</summary>
public class EmptyCommandsApiFactory : WebJeaApiFactory
{
    public const string HelpHtml = "Ask the <a href=\"https://helpdesk.example\">service desk</a>.";

    public EmptyCommandsApiFactory() : base(config =>
    {
        config["Commands"] = new JArray();
        config["NoAccessHelpHtml"] = HelpHtml;
    }, "Domain Admins")
    {
    }
}

/// <summary>Config without the required Commands property.</summary>
public class MissingCommandsApiFactory : WebJeaApiFactory
{
    public MissingCommandsApiFactory() : base(config => config.Remove("Commands"), "Domain Admins")
    {
    }
}

/// <summary>Adds a request that throws past every endpoint, to exercise the /api exception handler.</summary>
public class ThrowingApiFactory : WebJeaApiFactory
{
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Run(context => throw new InvalidOperationException("apitest unhandled exception"));
            };
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingStartupFilter>());
    }
}

public class ApiErrorHandlingTests :
    IClassFixture<WebJeaApiFactory>,
    IClassFixture<RestrictedCommandApiFactory>,
    IClassFixture<NoPermittedCommandsApiFactory>,
    IClassFixture<EmptyCommandsApiFactory>,
    IClassFixture<MissingCommandsApiFactory>,
    IClassFixture<ThrowingApiFactory>
{
    private const string NoAccessMessage = "Not available. Either no matching commands exist or you don't have permission to use them.";

    private readonly WebJeaApiFactory _global;
    private readonly RestrictedCommandApiFactory _restricted;
    private readonly NoPermittedCommandsApiFactory _noPermitted;
    private readonly EmptyCommandsApiFactory _emptyCommands;
    private readonly MissingCommandsApiFactory _missingCommands;
    private readonly ThrowingApiFactory _throwing;

    public ApiErrorHandlingTests(WebJeaApiFactory global, RestrictedCommandApiFactory restricted,
        NoPermittedCommandsApiFactory noPermitted, EmptyCommandsApiFactory emptyCommands,
        MissingCommandsApiFactory missingCommands, ThrowingApiFactory throwing)
    {
        _global = global;
        _restricted = restricted;
        _noPermitted = noPermitted;
        _emptyCommands = emptyCommands;
        _missingCommands = missingCommands;
        _throwing = throwing;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        LogCapture.Ensure();
        return client;
    }

    private static async Task<(int Status, JsonElement Body)> Get(WebApplicationFactory<Program> factory, string url)
    {
        var response = await CreateClient(factory).GetAsync(url);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ((int)response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<(int Status, JsonElement Body)> Execute(WebApplicationFactory<Program> factory, string cmdid)
    {
        var response = await CreateClient(factory).PostAsync("/api/execute",
            new StringContent("{\"cmdid\":\"" + cmdid + "\",\"parameters\":{}}", Encoding.UTF8, "application/json"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ((int)response.StatusCode, doc.RootElement.Clone());
    }

    private static string[] PropertyNames(JsonElement body)
    {
        return body.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
    }

    #region unknown vs denied commands look the same

    [Fact]
    public async Task CommandMetadata_UnknownAndDenied_ReturnIdenticalResponses()
    {
        var unknown = await Get(_restricted, "/api/command/nosuchcmd-metadata");
        var denied = await Get(_restricted, "/api/command/validate");
        var unknownGlobal = await Get(_global, "/api/command/nosuchcmd-metadata-global");

        foreach (var result in new[] { unknown, denied, unknownGlobal })
        {
            Assert.Equal(403, result.Status);
            Assert.Equal(NoAccessMessage, result.Body.GetProperty("error").GetString());
            Assert.False(string.IsNullOrEmpty(result.Body.GetProperty("requestId").GetString()));
            Assert.Equal(new[] { "error", "requestId" }, PropertyNames(result.Body));
        }
    }

    [Fact]
    public async Task Execute_UnknownAndDenied_ReturnIdenticalResponses()
    {
        var unknown = await Execute(_restricted, "nosuchcmd-execute");
        var denied = await Execute(_restricted, "validate");

        // global users skip the permission check, which used to give them "Command not found."
        var unknownGlobal = await Execute(_global, "nosuchcmd-execute-global");

        foreach (var result in new[] { unknown, denied, unknownGlobal })
        {
            Assert.Equal(403, result.Status);
            Assert.Equal(403, result.Body.GetProperty("status").GetInt32());
            Assert.Equal(NoAccessMessage, result.Body.GetProperty("statusmessage").GetString());
            Assert.Empty(result.Body.GetProperty("messages").EnumerateArray());
        }
    }

    [Fact]
    public async Task CommandMetadata_UnknownAndDenied_LogTheActualReason()
    {
        await Get(_restricted, "/api/command/nosuchcmd-logged");
        await Get(_restricted, "/api/command/validate");

        Assert.True(LogCapture.Contains("Warn|WebJEA.Api.CommandMetadataEndpoint|API: User DEV\\tester requested cmdid nosuchcmd-logged: not found in config"));
        Assert.True(LogCapture.Contains("Warn|WebJEA.Api.CommandMetadataEndpoint|API: User DEV\\tester denied cmdid validate: not in any permitted group"));
    }

    [Fact]
    public async Task Execute_UnknownAndDenied_LogTheActualReason()
    {
        await Execute(_global, "nosuchcmd-execute-logged");
        await Execute(_restricted, "validate");

        Assert.True(LogCapture.Contains("Warn|WebJEA.Api.ExecuteEndpoint|API: User DEV\\tester requested cmdid nosuchcmd-execute-logged: not found in config"));
        Assert.True(LogCapture.Contains("Warn|WebJEA.Api.ExecuteEndpoint|API: User DEV\\tester denied cmdid validate: not in any permitted group"));
    }

    [Fact]
    public async Task CommandMetadata_MixedCaseId_Resolves()
    {
        var result = await Get(_global, "/api/command/VALIDATE");

        Assert.Equal(200, result.Status);
        Assert.Equal("validate", result.Body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Execute_MixedCaseId_Resolves()
    {
        var result = await Execute(_global, "UTWP-Info");

        Assert.Equal(200, result.Status);
    }

    #endregion

    #region empty menu

    [Fact]
    public async Task Menu_NoCommandsConfigured_ReturnsEmptyAndLogsReason()
    {
        var result = await Get(_emptyCommands, "/api/menu");

        Assert.Equal(200, result.Status);
        Assert.Empty(result.Body.EnumerateArray());
        Assert.True(LogCapture.Contains("Warn|WebJEA.Api.MenuEndpoint|API: Menu empty for DEV\\tester: no commands are configured"));
    }

    [Fact]
    public async Task Menu_PermittedOnNone_ReturnsEmptyAndLogsReason()
    {
        var result = await Get(_noPermitted, "/api/menu");

        Assert.Equal(200, result.Status);
        Assert.Empty(result.Body.EnumerateArray());
        Assert.True(LogCapture.Matches(@"Warn\|WebJEA\.Api\.MenuEndpoint\|API: Menu empty for DEV\\tester: permitted on 0 of [1-9]\d* commands"));
    }

    [Fact]
    public async Task Menu_CommandsMissing_Returns500WithRequestId()
    {
        var result = await Get(_missingCommands, "/api/menu");

        Assert.Equal(500, result.Status);
        Assert.Equal("Internal server error: configuration failure.", result.Body.GetProperty("error").GetString());

        // the reference ID the user sees finds the log line that has the detail
        string requestId = result.Body.GetProperty("requestId").GetString();
        Assert.False(string.IsNullOrEmpty(requestId));
        Assert.True(LogCapture.Contains("Commands is required. Use \"Commands\": [] for a config with no commands.|" + requestId));
    }

    #endregion

    #region NoAccessHelpHtml

    [Fact]
    public async Task Config_NoAccessHelpHtmlSet_ReturnsIt()
    {
        var result = await Get(_emptyCommands, "/api/config");

        Assert.Equal(EmptyCommandsApiFactory.HelpHtml, result.Body.GetProperty("noAccessHelpHtml").GetString());
    }

    [Fact]
    public async Task Config_NoAccessHelpHtmlUnset_ReturnsNull()
    {
        var result = await Get(_global, "/api/config");

        Assert.Equal(JsonValueKind.Null, result.Body.GetProperty("noAccessHelpHtml").ValueKind);
    }

    #endregion

    #region unhandled exceptions

    [Fact]
    public async Task UnhandledApiException_ReturnsJsonWithRequestId()
    {
        var response = await CreateClient(_throwing).GetAsync("/api/does-not-exist");

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Internal server error.", doc.RootElement.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("requestId").GetString()));
    }

    #endregion
}
