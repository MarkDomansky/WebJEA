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
            dlog.Error("API: " + ex.Message + (ex.InnerException != null ? ": " + ex.InnerException.Message : ""));
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error: configuration failure." });
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

        await context.Response.WriteAsJsonAsync(menu);
    }
}
