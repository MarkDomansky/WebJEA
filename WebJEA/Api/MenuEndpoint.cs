using WebJEA.Api.Dto;

namespace WebJEA.Api;

/// <summary>GET /api/menu — commands available to the current user (dashboard tiles + sidebar).</summary>
public static class MenuEndpoint
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
            EndpointHelpers.LogConfigFailure(dlog, ex);
            await EndpointHelpers.WriteErrorAsync(context, 500, EndpointHelpers.ConfigFailureMessage);
            return;
        }

        IUserContext uinfo = EndpointHelpers.GetUserContext(context);

        List<MenuItemDto> menu = cmdSvc.Auth.GetMenu(uinfo)
            .Select(mi => new MenuItemDto
            {
                Id = mi.ID,
                DisplayName = mi.DisplayName,
                Description = mi.Description,
                Synopsis = mi.Synopsis,
                Uri = mi.Uri()
            })
            .ToList();

        // The UI shows the same "not available" message for both reasons; the log says which.
        if (menu.Count == 0)
        {
            int configured = cmdSvc.Config.Commands.Count;
            if (configured == 0)
            {
                dlog.Warn("API: Menu empty for " + uinfo.UserName + ": no commands are configured");
            }
            else
            {
                dlog.Warn("API: Menu empty for " + uinfo.UserName + ": permitted on 0 of " + configured + " commands");
            }
        }

        await context.Response.WriteAsJsonAsync(menu);
    }
}
