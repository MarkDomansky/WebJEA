using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using NLog.Web;
using WebJEA;
using WebJEA.Api;
using WebJEA.Auth;
using WebJEA.Auth.Dev;
using WebJEA.Auth.Entra;
using WebJEA.Auth.Windows;
using WebJEA.Hosting;
using WebJEA.Middleware;
using WebJEA.Telemetry;

// When running as a Windows service the process starts in System32; pin the content
// root to the exe directory so wwwroot/appsettings resolve. No-op everywhere else.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});

builder.Host.UseWindowsService();

builder.Logging.ClearProviders();
builder.Host.UseNLog();

// Listeners and the HTTPS certificate are native Kestrel configuration: Kestrel:Endpoints
// (written by Deploy.ps1 for the Windows service, cert selected by subject from
// LocalMachine\My), or ASPNETCORE_URLS + Kestrel:Certificates in containers.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.Configure<WebJeaOptions>(builder.Configuration.GetSection("WebJEA"));

// Native HTTP->HTTPS redirect (replaces the IIS URL Rewrite rule). The HTTPS port is
// discovered by the middleware itself: HTTPS_PORT if set, otherwise the single bound
// https address. With no HTTPS listener it logs one warning and serves HTTP unchanged.
builder.Services.Configure<HttpsRedirectionOptions>(options =>
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect);

bool isDevelopment = builder.Environment.IsDevelopment();
string authMode = builder.Configuration["Authentication:Mode"] ?? "Windows";
bool devAutoLogin = isDevelopment && builder.Configuration.GetValue<bool>("WebJEA:DevAutoLogin");

if (devAutoLogin)
{
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
    builder.Services.AddSingleton<IGroupResolver, DevGroupResolver>();
    builder.Services.AddSingleton<IUserContextFactory, ClaimsUserContextFactory>();
}
else if (string.Equals(authMode, "Entra", StringComparison.OrdinalIgnoreCase))
{
    builder.AddWebJeaEntraAuth();
}
else
{
    if (!OperatingSystem.IsWindows())
    {
        throw new InvalidOperationException(
            "Authentication:Mode 'Windows' requires a Windows host. Use 'Entra' on Linux.");
    }

    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddSingleton<IGroupResolver, WindowsGroupResolver>();
    builder.Services.AddSingleton<IUserContextFactory, WindowsUserContextFactory>();
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddScoped<WebJEA.IAuthorizationService>(_ => new AuthorizationService(isDevelopment));
builder.Services.AddScoped<CommandService>();
builder.Services.AddSingleton<ScriptExecutionService>();

builder.Services.AddSingleton<TelemetryChannel>();
builder.Services.AddHostedService<TelemetrySenderHostedService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "WebJEA.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Checked against the fully-composed configuration, before Kestrel binds in app.Run().
KestrelEndpoints.RejectLegacySettings(app.Configuration);

// Reverse-proxy path routing (container sub-site scenario): serve everything under
// e.g. /hr. Must be first in the pipeline.
string pathBase = app.Services.GetRequiredService<IOptions<WebJeaOptions>>().Value.PathBase;
if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase.StartsWith('/') ? pathBase : "/" + pathBase);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error.html");
}

// API callers get JSON, never the HTML error page, so the SPA can show a reference ID
// that matches the webjea.log entry. The middleware itself logs the exception.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), api =>
{
    api.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error.", requestId = EndpointHelpers.RequestId(context) });
    }));
});

// Before authentication so plain-HTTP requests are redirected instead of challenged.
app.UseHttpsRedirection();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<LegacyRedirectMiddleware>();

app.UseDefaultFiles();

app.UseAuthentication();
app.UseAuthorization();

// Require authentication for everything, including static files — parity with the
// IIS site running with Windows Authentication and anonymous access disabled.
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        await context.ChallengeAsync();
        return;
    }

    await next();
});

app.UseSession();
app.UseStaticFiles();

app.Map("/api/execute", ExecuteEndpoint.Handle);
app.MapGet("/api/config", AppInfoEndpoint.Handle);
app.MapGet("/api/menu", MenuEndpoint.Handle);
app.MapGet("/api/command/{cmdid}", CommandMetadataEndpoint.Handle);

app.Run();

public partial class Program { }
