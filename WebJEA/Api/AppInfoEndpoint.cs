using WebJEA.Api.Dto;

namespace WebJEA.Api;

/// <summary>
/// GET /api/config — app bootstrap for index.html/command.html. Also records the
/// page-load telemetry that default.aspx Page_Load used to send.
/// </summary>
public static class AppInfoEndpoint
{
    private static readonly NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public static async Task Handle(HttpContext context)
    {
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
        telemetry.AddIDs(uinfo.OrgId, uinfo.OrgName, "", uinfo.UserName);

        var dto = new AppInfoDto
        {
            Title = cmdSvc.Config.Title,
            HtmlLanguage = cmdSvc.Config.HtmlLanguage,
            Version = EndpointHelpers.AppVersion(),
            DashboardHtml = cmdSvc.Config.DashboardHtml,
            IsGlobalUser = cmdSvc.Auth.IsGlobalUser(uinfo),
            UserName = uinfo.UserName
        };

        if (cmdSvc.Config.SendTelemetry)
        {
            telemetry.SendTelemetry();
        }

        await context.Response.WriteAsJsonAsync(dto);
    }
}
