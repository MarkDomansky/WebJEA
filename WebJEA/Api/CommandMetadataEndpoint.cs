using WebJEA.Api.Dto;

namespace WebJEA.Api;

/// <summary>
/// GET /api/command/{cmdid} — form metadata consumed by form-renderer.js. Replaces
/// ControlBuilder's server-side control generation and records the page-load telemetry
/// that command.aspx Page_Load used to send.
/// </summary>
public static class CommandMetadataEndpoint
{
    private static readonly NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public static async Task Handle(HttpContext context)
    {
        string cmdid = context.GetRouteValue("cmdid") as string ?? "";

        CommandService cmdSvc;
        try
        {
            cmdSvc = EndpointHelpers.LoadCommandService(context);
        }
        catch (Exception ex)
        {
            dlog.Error("API: " + ex.Message + (ex.InnerException != null ? ": " + ex.InnerException.Message : ""));
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error: configuration failure." });
            return;
        }

        IUserContext uinfo = EndpointHelpers.GetUserContext(context);
        var telemetry = context.RequestServices.GetRequiredService<ITelemetryService>();

        EndpointHelpers.AddPageLoadTelemetry(context, telemetry, cmdSvc, uinfo);

        ConfigCmd cmd = cmdSvc.GetCommand(uinfo, cmdid);

        if (cmd == null)
        {
            telemetry.AddIDs(uinfo.OrgId, uinfo.OrgName, cmdid, uinfo.UserName, permitted: false);
            if (cmdSvc.Config.SendTelemetry) telemetry.SendTelemetry();

            dlog.Error("User " + uinfo.UserName + " requested cmdid " + cmdid + " that does not exist (or they don't have access to)");
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "You do not have access to this command." });
            return;
        }

        PSCmd scriptCmd = cmdSvc.GetScriptCmd(cmdid);
        PSCmd onloadCmd = cmdSvc.GetOnloadCmd(cmdid);

        telemetry.AddIDs(uinfo.OrgId, uinfo.OrgName, cmd.ID, uinfo.UserName);
        telemetry.Add("PermCount", cmd.PermittedGroups.Count);
        telemetry.Add("ParamCount", scriptCmd != null ? scriptCmd.Parameters.Count : 0);

        bool isGlobalUser = cmdSvc.Auth.IsGlobalUser(uinfo);
        CommandMetadataDto metadata = new FormMetadataBuilder()
            .Build(cmd, scriptCmd, onloadCmd, cmdSvc.Config.Title, isGlobalUser);

        if (cmdSvc.Config.SendTelemetry)
        {
            telemetry.SendTelemetry();
        }

        await context.Response.WriteAsJsonAsync(metadata);
    }
}
