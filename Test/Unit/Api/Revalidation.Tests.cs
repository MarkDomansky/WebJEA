using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebJEA.Auth;
using Xunit;

namespace WebJEA.Tests.Api;

/// <summary>Stands in for the Entra revalidator; each test scripts the result.</summary>
public sealed class FakeRevalidator : IUserRevalidator
{
    public RevalidationResult Result { get; set; }

    public int Calls { get; private set; }

    public Task<RevalidationResult> RevalidateAsync(HttpContext context)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

/// <summary>Non-global dev user; utwp-verbose is limited to "Helpdesk"; a fake revalidator decides who the user is.</summary>
public class RevalidatingApiFactory : WebJeaApiFactory
{
    public FakeRevalidator Revalidator { get; } = new FakeRevalidator();

    public RevalidatingApiFactory() : base(config => FixtureConfig.RestrictCommands(config, "Helpdesk", "utwp-verbose"), "Nobody")
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IUserRevalidator>(Revalidator));
    }
}

public class RevalidationTests : IClassFixture<RevalidatingApiFactory>
{
    private const string Command = "utwp-verbose";

    private readonly RevalidatingApiFactory _factory;

    public RevalidationTests(RevalidatingApiFactory factory)
    {
        _factory = factory;
    }

    private static IUserContext User(params string[] groups)
    {
        return new TestUserContext(new List<string>(groups), @"DEV\tester");
    }

    private async Task<(int Status, JsonElement Body)> Execute(RevalidationStatus status, IUserContext current, IUserContext signIn)
    {
        _factory.Revalidator.Result = new RevalidationResult(status, current, signIn);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync("/api/execute",
            new StringContent("{\"cmdid\":\"" + Command + "\",\"parameters\":{}}", Encoding.UTF8, "application/json"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ((int)response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Revoked_Returns401AndRunsNothing()
    {
        var result = await Execute(RevalidationStatus.Revoked, User("Helpdesk"), User("Helpdesk"));

        Assert.Equal(401, result.Status);
        Assert.Equal("Your sign-in is no longer valid. Reload the page to sign in again.", result.Body.GetProperty("statusmessage").GetString());
        Assert.Equal(JsonValueKind.Null, result.Body.GetProperty("output").ValueKind);
        Assert.False(result.Body.TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task Unavailable_Returns503AndRunsNothing()
    {
        var result = await Execute(RevalidationStatus.Unavailable, User("Helpdesk"), User("Helpdesk"));

        Assert.Equal(503, result.Status);
        Assert.Equal("Couldn't verify your sign-in. Try again in a few minutes.", result.Body.GetProperty("statusmessage").GetString());
        Assert.Equal(JsonValueKind.Null, result.Body.GetProperty("output").ValueKind);
    }

    [Fact]
    public async Task CurrentGroupsGrantAccess_EvenWhenSignInGroupsDidNot()
    {
        var result = await Execute(RevalidationStatus.Valid, User("Helpdesk"), User("Nobody"));

        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task AccessRemovedSinceSignIn_Returns403WithAccessChangedReason()
    {
        var result = await Execute(RevalidationStatus.Valid, User("Nobody"), User("Helpdesk"));

        Assert.Equal(403, result.Status);
        Assert.Equal("accesschanged", result.Body.GetProperty("reason").GetString());
        Assert.Equal("Your access has changed. You no longer have permission to run this command.",
            result.Body.GetProperty("statusmessage").GetString());
    }

    [Fact]
    public async Task NeverPermitted_ReturnsPlain403()
    {
        var result = await Execute(RevalidationStatus.Valid, User("Nobody"), User("Nobody"));

        Assert.Equal(403, result.Status);
        Assert.False(result.Body.TryGetProperty("reason", out _));
        Assert.Equal(new[] { "messages", "output", "requestId", "status", "statusmessage" },
            result.Body.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
    }
}
