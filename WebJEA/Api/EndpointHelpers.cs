using System.Reflection;
using Microsoft.Extensions.Options;

namespace WebJEA.Api;

internal static class EndpointHelpers
{
    /// <summary>
    /// The single response for a command that doesn't exist and for one the user isn't
    /// permitted on, so the API never reveals which command Ids exist. The log records
    /// which of the two it was (<see cref="LogAccessFailure"/>).
    /// </summary>
    public const string NoAccessMessage = "Not available. Either no matching commands exist or you don't have permission to use them.";

    public const string ConfigFailureMessage = "Internal server error: configuration failure.";

    /// <summary>Execute refused because the user's sign-in is no longer valid (they've been signed out).</summary>
    public const string SignInAgainMessage = "Your sign-in is no longer valid. Reload the page to sign in again.";

    /// <summary>Execute refused because the user's sign-in couldn't be verified with the identity provider.</summary>
    public const string VerifyUnavailableMessage = "Couldn't verify your sign-in. Try again in a few minutes.";

    /// <summary>Execute refused because the user lost access to the command since signing in.</summary>
    public const string AccessChangedMessage = "Your access has changed. You no longer have permission to run this command.";

    /// <summary>
    /// The ID webjea.log records on every line for this request, so a user's reference ID
    /// finds the log entry. NLog's ${aspnet-TraceIdentifier} prefers the W3C activity ID
    /// over HttpContext.TraceIdentifier, so this does too.
    /// </summary>
    public static string RequestId(HttpContext context)
    {
        return System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
    }

    /// <summary>Writes the error body the GET endpoints share.</summary>
    public static Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error = message, requestId = RequestId(context) });
    }

    public static void LogConfigFailure(NLog.Logger log, Exception ex)
    {
        log.Error("API: " + ex.Message + (ex.InnerException != null ? ": " + ex.InnerException.Message : ""));
    }

    public static void LogAccessFailure(NLog.Logger log, IUserContext uinfo, string cmdid, CommandAccess access)
    {
        if (access == CommandAccess.NotFound)
        {
            log.Warn("API: User " + uinfo.UserName + " requested cmdid " + cmdid + ": not found in config");
        }
        else
        {
            log.Warn("API: User " + uinfo.UserName + " denied cmdid " + cmdid + ": not in any permitted group");
        }
    }

    /// <summary>Config lower-cases command Ids (ConfigCmd.ID), so requests must too.</summary>
    public static string NormalizeCommandId(string cmdid)
    {
        return (cmdid ?? "").ToLowerInvariant();
    }

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
