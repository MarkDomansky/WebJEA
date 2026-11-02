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
        string cmdid = EndpointHelpers.NormalizeCommandId(context.GetRouteValue("cmdid") as string);

        CommandService cmdSvc;
        try
        {
            cmdSvc = EndpointHelpers.LoadCommandService(context);
        }
        catch (Exception ex)
        {
            EndpointHelpers.LogConfigFailure(dlog, ex);
            await EndpointHelpers.WriteErrorAsync(context, 500, EndpointHelpers.ConfigFailureMessage);
            return;
        }

        IUserContext uinfo = EndpointHelpers.GetUserContext(context);
        var telemetry = context.RequestServices.GetRequiredService<ITelemetryService>();

        EndpointHelpers.AddPageLoadTelemetry(context, telemetry, cmdSvc, uinfo);

        CommandAccess access = cmdSvc.Auth.CheckCommandAccess(uinfo, cmdid);
        if (access != CommandAccess.Allowed)
        {
            telemetry.AddIDs(uinfo.OrgId, uinfo.OrgName, cmdid, uinfo.UserName, permitted: false);
            if (cmdSvc.Config.SendTelemetry) telemetry.SendTelemetry();

            // Same 403 for unknown and denied Ids; only the log says which.
            EndpointHelpers.LogAccessFailure(dlog, uinfo, cmdid, access);
            await EndpointHelpers.WriteErrorAsync(context, 403, EndpointHelpers.NoAccessMessage);
            return;
        }

        ConfigCmd cmd = cmdSvc.GetCommand(uinfo, cmdid);

        PSCmd scriptCmd = cmdSvc.GetScriptCmd(cmdid);
        PSCmd onloadCmd = cmdSvc.GetOnloadCmd(cmdid);

        telemetry.AddIDs(uinfo.OrgId, uinfo.OrgName, cmd.ID, uinfo.UserName);
        telemetry.Add("PermCount", cmd.PermittedGroups.Count);
        telemetry.Add("ParamCount", scriptCmd != null ? scriptCmd.Parameters.Count : 0);

        bool isGlobalUser = cmdSvc.Auth.IsGlobalUser(uinfo);
        CommandMetadataDto metadata = new FormMetadataBuilder()
            .Build(cmd, scriptCmd, onloadCmd, cmdSvc.Config.Title, isGlobalUser,
                   cmdSvc.Config.ShowVerbose, cmdSvc.Config.RenderMode);

        if (cmdSvc.Config.SendTelemetry)
        {
            telemetry.SendTelemetry();
        }

        await context.Response.WriteAsJsonAsync(metadata);
    }
}
