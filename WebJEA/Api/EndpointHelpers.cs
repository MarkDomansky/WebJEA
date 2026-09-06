using System.Reflection;
using Microsoft.Extensions.Options;

namespace WebJEA.Api;

internal static class EndpointHelpers
{
    /// <summary>Loads the WebJEA config for this request (per-request reload, parity with WebForms pages).</summary>
    public static CommandService LoadCommandService(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<WebJeaOptions>>().Value;
        var resolver = context.RequestServices.GetRequiredService<IGroupResolver>();
        var svc = context.RequestServices.GetRequiredService<CommandService>();

        string configFile = options.ConfigFile;
        if (!string.IsNullOrEmpty(configFile) && !Path.IsPathRooted(configFile))
        {
            var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
            configFile = Path.GetFullPath(Path.Combine(env.ContentRootPath, configFile));
        }

        svc.LoadConfig(configFile, resolver);
        return svc;
    }

    public static IUserContext GetUserContext(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<IUserContextFactory>().Create(context.User);
    }

    /// <summary>
    /// The version the build stamped in (CI passes semantic-release's version as
    /// -p:Version), so prereleases read e.g. "2100.0.0-alpha.1" rather than the
    /// numeric-only assembly version. Falls back to the assembly version.
    /// </summary>
    public static string AppVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Strip any "+<build metadata>" the SDK may append.
                int plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>The page-load telemetry previously recorded in default.aspx/command.aspx Page_Load.</summary>
    public static void AddPageLoadTelemetry(HttpContext context, ITelemetryService telemetry, CommandService svc, IUserContext uinfo)
    {
        telemetry.Add("sessionid", Helpers.StringHash256(GetSessionId(context)));
        telemetry.Add("requestid", Helpers.StringHash256(Guid.NewGuid().ToString()));
        telemetry.Add("CommandCount", svc.Config.Commands.Count);
        telemetry.Add("PermGlobalCount", svc.Config.PermittedGroups.Count);
        telemetry.Add("IsGlobalUser", svc.Auth.IsGlobalUser(uinfo));

        string version = AppVersion();
        if (version != "")
        {
            telemetry.Add("appedition", "CE");
            telemetry.Add("appversion", version);
        }
    }

    private static string GetSessionId(HttpContext context)
    {
        try
        {
            // setting a value pins the session id, like Session("init") = 0 did in Global.asax
            context.Session.SetInt32("init", 0);
            return context.Session.Id;
        }
        catch
        {
            return "";
        }
    }
}
