using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WebJEA.Auth.Dev;

/// <summary>
/// Development-only authentication: every request is authenticated as the configured
/// DevUser (DevUser:Name + DevUser:Sids). Replaces the old "#If Not DEBUG" auth bypass
/// with an explicit, environment-gated scheme.
/// </summary>
public class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAutoLogin";

    private readonly IConfiguration _configuration;

    public DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
                          ILoggerFactory logger,
                          UrlEncoder encoder,
                          IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string name = _configuration["DevUser:Name"] ?? @"DEV\tester";
        string[] sids = _configuration.GetSection("DevUser:Sids").Get<string[]>() ?? Array.Empty<string>();

        var claims = new List<Claim> { new Claim(ClaimTypes.Name, name) };
        foreach (string sid in sids)
        {
            claims.Add(new Claim(ClaimTypes.GroupSid, sid));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
