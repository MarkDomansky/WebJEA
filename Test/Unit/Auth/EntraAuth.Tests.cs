using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using WebJEA;
using WebJEA.Auth;
using WebJEA.Auth.Entra;
using Xunit;

namespace WebJEA.Tests.Auth;

public class EntraUserContextTests
{
    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestEntra"));
    }

    [Fact]
    public void GroupsClaims_BecomeLowercasedMemberOfIds()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("groups", "AAAAAAAA-1111-2222-3333-444444444444"),
            new Claim("groups", "bbbbbbbb-1111-2222-3333-444444444444"));

        var ctx = new EntraUserContext(principal, null);

        Assert.Contains("aaaaaaaa-1111-2222-3333-444444444444", ctx.MemberOfIds);
        Assert.Contains("bbbbbbbb-1111-2222-3333-444444444444", ctx.MemberOfIds);
    }

    [Fact]
    public void UserName_PrefersPreferredUsername()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim(ClaimTypes.Name, "Display Name"));

        var ctx = new EntraUserContext(principal, null);

        Assert.Equal("user@contoso.com", ctx.UserName);
    }

    [Fact]
    public void ObjectIdAndUpn_AreIncludedInMemberOfIds()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("oid", "CCCCCCCC-1111-2222-3333-444444444444"));

        var ctx = new EntraUserContext(principal, null);

        Assert.Contains("cccccccc-1111-2222-3333-444444444444", ctx.MemberOfIds);
        Assert.Contains("user@contoso.com", ctx.MemberOfIds);
    }

    [Fact]
    public void Upn_IsLowercasedInMemberOfIds_ButUserNameKeepsOriginalCase()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "User@Contoso.COM"));

        var ctx = new EntraUserContext(principal, null);

        Assert.Equal("User@Contoso.COM", ctx.UserName);
        Assert.Contains("user@contoso.com", ctx.MemberOfIds);
    }

    [Fact]
    public void RolesClaims_BecomeLowercasedMemberOfIds()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("roles", "ScriptExecutor"),
            new Claim(ClaimTypes.Role, "Admin"));

        var ctx = new EntraUserContext(principal, null);

        Assert.Contains("scriptexecutor", ctx.MemberOfIds);
        Assert.Contains("admin", ctx.MemberOfIds);
        Assert.True(ctx.IsMemberOf("scriptexecutor"));
    }

    private class FakeGraphGroupLoader : GraphGroupLoader
    {
        public FakeGraphGroupLoader() : base(null) { }

        public override List<string> GetTransitiveGroupIds()
        {
            return new List<string> { "FFFFFFFF-1111-2222-3333-444444444444" };
        }
    }

    [Fact]
    public void GroupOverage_WithRolesPresent_StillFallsBackToGraph()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("roles", "ScriptExecutor"),
            new Claim("_claim_names", "{\"groups\":\"src1\"}"));

        var ctx = new EntraUserContext(principal, new FakeGraphGroupLoader());

        Assert.Contains("ffffffff-1111-2222-3333-444444444444", ctx.MemberOfIds);
        Assert.Contains("scriptexecutor", ctx.MemberOfIds);
    }

    [Fact]
    public void IsMemberOf_MatchesWildcardAndIds()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("groups", "aaaaaaaa-1111-2222-3333-444444444444"));

        var ctx = new EntraUserContext(principal, null);

        Assert.True(ctx.IsMemberOf("*"));
        Assert.True(ctx.IsMemberOf("aaaaaaaa-1111-2222-3333-444444444444"));
        Assert.False(ctx.IsMemberOf("dddddddd-1111-2222-3333-444444444444"));
    }

    [Fact]
    public void TenantId_BecomesOrgId()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("tid", "eeeeeeee-1111-2222-3333-444444444444"));

        var ctx = new EntraUserContext(principal, null);

        Assert.Equal("eeeeeeee-1111-2222-3333-444444444444", ctx.OrgId);
        Assert.Equal("contoso.com", ctx.OrgName);
    }

    [Fact]
    public void GroupOverage_WithoutGraph_LeavesGroupsEmpty()
    {
        var principal = BuildPrincipal(
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("_claim_names", "{\"groups\":\"src1\"}"));

        var ctx = new EntraUserContext(principal, null);

        Assert.False(ctx.IsMemberOf("aaaaaaaa-1111-2222-3333-444444444444"));
    }
}

public class EntraGroupResolverTests
{
    private static EntraGroupResolver BuildResolver(bool resolveNames = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Authentication:Entra:ResolveGroupNamesViaGraph"] = resolveNames.ToString()
            })
            .Build();

        var services = new ServiceCollection().BuildServiceProvider();
        return new EntraGroupResolver(config, services);
    }

    [Fact]
    public void Wildcard_PassesThrough()
    {
        Assert.Equal("*", BuildResolver().GetSID("*"));
    }

    [Fact]
    public void Guid_IsNormalizedToLowercase()
    {
        Assert.Equal("aaaaaaaa-1111-2222-3333-444444444444",
            BuildResolver().GetSID("AAAAAAAA-1111-2222-3333-444444444444"));
    }

    [Fact]
    public void GuidWithBraces_IsNormalized()
    {
        Assert.Equal("aaaaaaaa-1111-2222-3333-444444444444",
            BuildResolver().GetSID("{AAAAAAAA-1111-2222-3333-444444444444}"));
    }

    [Fact]
    public void NonGuid_PassesThroughLowercased_AsRoleOrUpnLiteral()
    {
        Assert.Equal("scriptexecutor", BuildResolver().GetSID("ScriptExecutor"));
        Assert.Equal("alice@contoso.com", BuildResolver().GetSID("Alice@Contoso.com"));
    }

    [Fact]
    public void DisplayName_WithoutGraph_PassesThroughLowercased()
    {
        Assert.Equal("my admins", BuildResolver().GetSID("My Admins"));
    }

    [Fact]
    public void DisplayName_WithResolveNamesButNoLoader_FallsBackToLiteral()
    {
        Assert.Equal("my admins", BuildResolver(resolveNames: true).GetSID("My Admins"));
    }

    private class CountingGraphGroupLoader : GraphGroupLoader
    {
        public List<string> Lookups { get; } = new List<string>();

        public CountingGraphGroupLoader() : base(null) { }

        public override string FindGroupIdByDisplayName(string displayName)
        {
            Lookups.Add(displayName);
            return displayName == "My Admins" ? "DDDDDDDD-1111-2222-3333-444444444444" : "";
        }
    }

    private static EntraGroupResolver BuildResolverWithLoader(GraphGroupLoader loader, string appRolePrefix = null)
    {
        var settings = new Dictionary<string, string>(); // ResolveGroupNamesViaGraph unset: defaults to true
        if (appRolePrefix != null) settings["Authentication:Entra:AppRolePrefix"] = appRolePrefix;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton(loader).BuildServiceProvider();
        return new EntraGroupResolver(config, services);
    }

    [Fact]
    public void DisplayName_ResolvesViaGraph_ByDefault()
    {
        var loader = new CountingGraphGroupLoader();

        Assert.Equal("dddddddd-1111-2222-3333-444444444444", BuildResolverWithLoader(loader).GetSID("My Admins"));
        Assert.Equal(new[] { "My Admins" }, loader.Lookups);
    }

    [Theory]
    [InlineData("alice@contoso.com")]
    [InlineData("Alice.Smith@sub.contoso.co.uk")]
    public void Upn_SkipsGraphLookup(string upn)
    {
        var loader = new CountingGraphGroupLoader();

        Assert.Equal(upn.ToLowerInvariant(), BuildResolverWithLoader(loader).GetSID(upn));
        Assert.Empty(loader.Lookups);
    }

    [Theory]
    [InlineData("WebJEA.HR")]
    [InlineData("webjea.admin")]
    public void AppRolePrefix_SkipsGraphLookup(string role)
    {
        var loader = new CountingGraphGroupLoader();

        Assert.Equal(role.ToLowerInvariant(), BuildResolverWithLoader(loader, "WebJEA.").GetSID(role));
        Assert.Empty(loader.Lookups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoAppRolePrefix_RoleValuesAreStillLookedUp(string prefix)
    {
        var loader = new CountingGraphGroupLoader();

        BuildResolverWithLoader(loader, prefix).GetSID("WebJEA.HR");
        Assert.Equal(new[] { "WebJEA.HR" }, loader.Lookups);
    }

    [Fact]
    public void AppRolePrefix_DoesNotAffectOtherNames()
    {
        var loader = new CountingGraphGroupLoader();

        Assert.Equal("dddddddd-1111-2222-3333-444444444444",
            BuildResolverWithLoader(loader, "WebJEA.").GetSID("My Admins"));
        Assert.Equal(new[] { "My Admins" }, loader.Lookups);
    }

    [Theory]
    [InlineData("Help Desk @ HQ")]
    [InlineData("admins@contoso")]
    public void AtSignButNotUpn_IsStillLookedUp(string name)
    {
        var loader = new CountingGraphGroupLoader();

        BuildResolverWithLoader(loader).GetSID(name);
        Assert.Equal(new[] { name }, loader.Lookups);
    }

    [Theory]
    [InlineData(@".\Administrators")]
    [InlineData(@"CONTOSO\HR Admins")]
    [InlineData(@"BUILTIN\Administrators")]
    public void WindowsStyle_SkipsGraphLookup(string entry)
    {
        var loader = new CountingGraphGroupLoader();

        Assert.Equal(entry.ToLowerInvariant(), BuildResolverWithLoader(loader).GetSID(entry));
        Assert.Empty(loader.Lookups);
    }
}

// Uses GraphGroupLoader's process-wide consent warning flag; xUnit runs a class's tests sequentially,
// and no other test class calls the base Graph lookup methods that set it
public class GraphGroupLoaderConsentTests
{
    private static Exception ConsentException()
    {
        return new MsalUiRequiredException("invalid_grant",
            "AADSTS65001: The user or administrator has not consented to use the application.");
    }

    private class ScriptedGraphGroupLoader : GraphGroupLoader
    {
        private readonly Exception _failWith;

        public int Queries { get; private set; }

        public ScriptedGraphGroupLoader(Exception failWith = null) : base(null)
        {
            _failWith = failWith;
        }

        protected override string QueryGroupIdByDisplayName(string displayName)
        {
            Queries++;
            if (_failWith != null) throw _failWith;
            return "dddddddd-1111-2222-3333-444444444444";
        }

        protected override List<string> QueryTransitiveGroupIds()
        {
            Queries++;
            if (_failWith != null) throw _failWith;
            return new List<string> { "dddddddd-1111-2222-3333-444444444444" };
        }
    }

    public GraphGroupLoaderConsentTests()
    {
        GraphGroupLoader.ResetConsentWarning();
    }

    [Fact]
    public void IsConsentRequired_MatchesDirectAndWrappedConsentFailures()
    {
        Assert.True(GraphGroupLoader.IsConsentRequired(ConsentException()));
        Assert.True(GraphGroupLoader.IsConsentRequired(
            new InvalidOperationException("IDW10502: An MsalUiRequiredException was thrown", ConsentException())));
        Assert.False(GraphGroupLoader.IsConsentRequired(new InvalidOperationException("network down")));
    }

    [Fact]
    public void ConsentFailure_WarnsAndSkipsRestOfRequest()
    {
        var loader = new ScriptedGraphGroupLoader(ConsentException());

        Assert.Equal("", loader.FindGroupIdByDisplayName("My Admins"));
        Assert.True(GraphGroupLoader.ConsentWarningLogged);

        Assert.Equal("", loader.FindGroupIdByDisplayName("HR Admins"));
        Assert.Empty(loader.GetTransitiveGroupIds());
        Assert.Equal(1, loader.Queries);
    }

    [Fact]
    public void ConsentFailure_DoesNotCarryOverToNextRequest()
    {
        new ScriptedGraphGroupLoader(ConsentException()).FindGroupIdByDisplayName("My Admins");

        var next = new ScriptedGraphGroupLoader(ConsentException());
        next.FindGroupIdByDisplayName("My Admins");

        Assert.Equal(1, next.Queries);
    }

    [Fact]
    public void Success_ClearsConsentWarning()
    {
        new ScriptedGraphGroupLoader(ConsentException()).FindGroupIdByDisplayName("My Admins");
        Assert.True(GraphGroupLoader.ConsentWarningLogged);

        Assert.Equal("dddddddd-1111-2222-3333-444444444444",
            new ScriptedGraphGroupLoader().FindGroupIdByDisplayName("My Admins"));
        Assert.False(GraphGroupLoader.ConsentWarningLogged);
    }

    [Fact]
    public void Revalidation_Success_ReturnsIds()
    {
        var lookup = new ScriptedGraphGroupLoader().LoadTransitiveGroupIdsForRevalidation();

        Assert.Equal(GroupLookupStatus.Ok, lookup.Status);
        Assert.Equal(new[] { "dddddddd-1111-2222-3333-444444444444" }, lookup.Ids);
    }

    [Fact]
    public void Revalidation_ConsentFailure_IsUnavailable_AndWarns()
    {
        var lookup = new ScriptedGraphGroupLoader(ConsentException()).LoadTransitiveGroupIdsForRevalidation();

        Assert.Equal(GroupLookupStatus.Unavailable, lookup.Status);
        Assert.Equal("AADSTS65001", lookup.Detail);
        Assert.True(GraphGroupLoader.ConsentWarningLogged);
    }

    [Fact]
    public void Revalidation_AfterConsentFailureInSameRequest_SkipsGraph()
    {
        var loader = new ScriptedGraphGroupLoader(ConsentException());
        loader.FindGroupIdByDisplayName("My Admins");

        Assert.Equal(GroupLookupStatus.Unavailable, loader.LoadTransitiveGroupIdsForRevalidation().Status);
        Assert.Equal(1, loader.Queries);
    }

    [Fact]
    public void Revalidation_RevokedSignIn_IsSignInRequired()
    {
        var revoked = new InvalidOperationException("IDW10502: An MsalUiRequiredException was thrown",
            new MsalUiRequiredException("invalid_grant", "AADSTS50173: The provided grant has expired due to it being revoked."));

        var lookup = new ScriptedGraphGroupLoader(revoked).LoadTransitiveGroupIdsForRevalidation();

        Assert.Equal(GroupLookupStatus.SignInRequired, lookup.Status);
        Assert.Equal("invalid_grant", lookup.Detail);
        Assert.False(GraphGroupLoader.ConsentWarningLogged);
    }

    [Fact]
    public void OtherFailure_NeitherWarnsNorShortCircuits()
    {
        var loader = new ScriptedGraphGroupLoader(new InvalidOperationException("network down"));

        Assert.Equal("", loader.FindGroupIdByDisplayName("My Admins"));
        Assert.Equal("", loader.FindGroupIdByDisplayName("HR Admins"));
        Assert.False(GraphGroupLoader.ConsentWarningLogged);
        Assert.Equal(2, loader.Queries);
    }
}

public class GraphFailureClassificationTests
{
    [Fact]
    public void MsalUiRequired_AnywhereInChain_IsSignInRequired()
    {
        Assert.Equal(GroupLookupStatus.SignInRequired, GraphGroupLoader.ClassifyFailure(
            new MsalUiRequiredException("user_null", "No account or login hint was passed")));
        Assert.Equal(GroupLookupStatus.SignInRequired, GraphGroupLoader.ClassifyFailure(
            new InvalidOperationException("IDW10502", new MsalUiRequiredException("invalid_grant", "AADSTS50173"))));
    }

    [Fact]
    public void Graph401_IsSignInRequired()
    {
        Assert.Equal(GroupLookupStatus.SignInRequired, GraphGroupLoader.ClassifyFailure(
            new ODataError { ResponseStatusCode = 401 }));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public void OtherGraphStatus_IsUnavailable(int status)
    {
        Assert.Equal(GroupLookupStatus.Unavailable, GraphGroupLoader.ClassifyFailure(
            new ODataError { ResponseStatusCode = status }));
    }

    [Fact]
    public void ConsentAndNetworkFailures_AreUnavailable()
    {
        Assert.Equal(GroupLookupStatus.Unavailable, GraphGroupLoader.ClassifyFailure(
            new MsalUiRequiredException("invalid_grant", "AADSTS65001: The user or administrator has not consented")));
        Assert.Equal(GroupLookupStatus.Unavailable, GraphGroupLoader.ClassifyFailure(
            new HttpRequestException("No such host is known.")));
    }
}

public class EntraUserContextVariantTests
{
    private static ClaimsPrincipal Principal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("oid", "CCCCCCCC-1111-2222-3333-444444444444"),
            new Claim("groups", "aaaaaaaa-1111-2222-3333-444444444444"),
            new Claim("roles", "ScriptExecutor")
        }, "TestEntra"));
    }

    [Fact]
    public void FromGraphGroups_ReplacesTokenGroups_KeepsRolesOidAndUpn()
    {
        var ctx = EntraUserContext.FromGraphGroups(Principal(), new[] { "BBBBBBBB-1111-2222-3333-444444444444" });

        Assert.Contains("bbbbbbbb-1111-2222-3333-444444444444", ctx.MemberOfIds);
        Assert.DoesNotContain("aaaaaaaa-1111-2222-3333-444444444444", ctx.MemberOfIds);
        Assert.Contains("scriptexecutor", ctx.MemberOfIds);
        Assert.Contains("cccccccc-1111-2222-3333-444444444444", ctx.MemberOfIds);
        Assert.Contains("user@contoso.com", ctx.MemberOfIds);
    }

    [Fact]
    public void FromTokenOnly_UsesTokenGroups()
    {
        Assert.Contains("aaaaaaaa-1111-2222-3333-444444444444", EntraUserContext.FromTokenOnly(Principal()).MemberOfIds);
    }

    [Fact]
    public void FromTokenOnly_WithOverage_HasNoGroups()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("_claim_names", "{\"groups\":\"src1\"}")
        }, "TestEntra"));

        Assert.Equal(new[] { "user@contoso.com" }, EntraUserContext.FromTokenOnly(principal).MemberOfIds);
    }
}

public class EntraRevalidatorTests
{
    private const string Fresh = "bbbbbbbb-1111-2222-3333-444444444444";

    private static ClaimsPrincipal Principal(string oid = "cccccccc-1111-2222-3333-444444444444")
    {
        var claims = new List<Claim>
        {
            new Claim("preferred_username", "user@contoso.com"),
            new Claim("tid", "eeeeeeee-1111-2222-3333-444444444444"),
            new Claim("groups", "aaaaaaaa-1111-2222-3333-444444444444")
        };
        if (oid != null) claims.Add(new Claim("oid", oid));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestEntra"));
    }

    private static IConfiguration Config(int? cacheSeconds = null, bool failOpen = false)
    {
        var settings = new Dictionary<string, string> { ["Authentication:Entra:RevalidateFailOpen"] = failOpen.ToString() };
        if (cacheSeconds != null) settings["Authentication:Entra:RevalidateCacheSeconds"] = cacheSeconds.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static RevalidationCache Cache(int? cacheSeconds = null)
    {
        return new RevalidationCache(new MemoryCache(new MemoryCacheOptions()), Config(cacheSeconds));
    }

    private class ScriptedLoader : GraphGroupLoader
    {
        public GroupLookup Next { get; set; } = new GroupLookup(GroupLookupStatus.Ok, new[] { Fresh }, "");

        public int Calls { get; private set; }

        public ScriptedLoader() : base(null) { }

        public override GroupLookup LoadTransitiveGroupIdsForRevalidation()
        {
            Calls++;
            return Next;
        }

        public override List<string> GetTransitiveGroupIds() => new List<string>();
    }

    private sealed class RecordingAuthService : IAuthenticationService
    {
        public List<string> SignedOut { get; } = new List<string>();

        public Task SignOutAsync(HttpContext context, string scheme, AuthenticationProperties properties)
        {
            SignedOut.Add(scheme);
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string scheme, AuthenticationProperties properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string scheme, AuthenticationProperties properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties) => throw new NotSupportedException();
    }

    private static (EntraRevalidator Revalidator, ScriptedLoader Loader, RevalidationCache Cache) Build(int? cacheSeconds = null, bool failOpen = false)
    {
        var loader = new ScriptedLoader();
        var cache = Cache(cacheSeconds);
        return (new EntraRevalidator(loader, cache, Config(cacheSeconds, failOpen)), loader, cache);
    }

    private static (HttpContext Context, RecordingAuthService Auth) Request(ClaimsPrincipal principal = null)
    {
        var auth = new RecordingAuthService();
        var context = new DefaultHttpContext
        {
            User = principal ?? Principal(),
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider()
        };
        return (context, auth);
    }

    [Fact]
    public async Task Valid_UsesCurrentGroups_AndCachesThem()
    {
        var (revalidator, loader, cache) = Build();
        var (context, _) = Request();

        var result = await revalidator.RevalidateAsync(context);

        Assert.Equal(RevalidationStatus.Valid, result.Status);
        Assert.Contains(Fresh, result.User.MemberOfIds);
        Assert.DoesNotContain("aaaaaaaa-1111-2222-3333-444444444444", result.User.MemberOfIds);
        Assert.Contains("aaaaaaaa-1111-2222-3333-444444444444", result.SignInUser.MemberOfIds);
        Assert.True(cache.TryGet(context.User, out _));
        Assert.Equal(1, loader.Calls);
    }

    [Fact]
    public async Task CacheHit_SkipsGraph()
    {
        var (revalidator, loader, _) = Build();

        await revalidator.RevalidateAsync(Request().Context);
        var result = await revalidator.RevalidateAsync(Request().Context);

        Assert.Equal(RevalidationStatus.Valid, result.Status);
        Assert.Contains(Fresh, result.User.MemberOfIds);
        Assert.Equal(1, loader.Calls);
    }

    [Fact]
    public async Task CacheSecondsZero_AlwaysCallsGraph()
    {
        var (revalidator, loader, _) = Build(cacheSeconds: 0);

        await revalidator.RevalidateAsync(Request().Context);
        await revalidator.RevalidateAsync(Request().Context);

        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task NoObjectId_IsNotCached()
    {
        var (revalidator, loader, _) = Build();

        await revalidator.RevalidateAsync(Request(Principal(oid: null)).Context);
        await revalidator.RevalidateAsync(Request(Principal(oid: null)).Context);

        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task SignInRequired_SignsOutAndEvicts()
    {
        var (revalidator, loader, cache) = Build(cacheSeconds: 0);
        var (context, auth) = Request();
        loader.Next = new GroupLookup(GroupLookupStatus.SignInRequired, Array.Empty<string>(), "invalid_grant");

        var result = await revalidator.RevalidateAsync(context);

        Assert.Equal(RevalidationStatus.Revoked, result.Status);
        Assert.Equal(new[] { CookieAuthenticationDefaults.AuthenticationScheme }, auth.SignedOut);
        Assert.False(cache.TryGet(context.User, out _));
    }

    [Fact]
    public async Task Unavailable_FailsClosedByDefault()
    {
        var (revalidator, loader, cache) = Build();
        var (context, auth) = Request();
        loader.Next = new GroupLookup(GroupLookupStatus.Unavailable, Array.Empty<string>(), "Graph HTTP 503");

        var result = await revalidator.RevalidateAsync(context);

        Assert.Equal(RevalidationStatus.Unavailable, result.Status);
        Assert.Empty(auth.SignedOut);
        Assert.False(cache.TryGet(context.User, out _));
    }

    [Fact]
    public async Task Unavailable_WithFailOpen_UsesSignInClaims()
    {
        var (revalidator, loader, _) = Build(failOpen: true);
        loader.Next = new GroupLookup(GroupLookupStatus.Unavailable, Array.Empty<string>(), "Graph HTTP 503");

        var result = await revalidator.RevalidateAsync(Request().Context);

        Assert.Equal(RevalidationStatus.Valid, result.Status);
        Assert.Contains("aaaaaaaa-1111-2222-3333-444444444444", result.User.MemberOfIds);
    }

    [Fact]
    public void UserContextFactory_PrefersCachedGroups()
    {
        var cache = Cache();
        var services = new ServiceCollection().AddSingleton(cache).BuildServiceProvider();
        var factory = new EntraUserContextFactory(services);

        Assert.Contains("aaaaaaaa-1111-2222-3333-444444444444", factory.Create(Principal()).MemberOfIds);

        cache.Set(Principal(), new[] { Fresh });
        var ctx = factory.Create(Principal());

        Assert.Contains(Fresh, ctx.MemberOfIds);
        Assert.DoesNotContain("aaaaaaaa-1111-2222-3333-444444444444", ctx.MemberOfIds);
    }
}

public class EntraRegistrationTests
{
    private static WebApplicationBuilder Build(Dictionary<string, string> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = "eeeeeeee-1111-2222-3333-444444444444",
            ["AzureAd:ClientId"] = "ffffffff-1111-2222-3333-444444444444"
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddWebJeaEntraAuth();
        return builder;
    }

    private static bool HasRevalidator(WebApplicationBuilder builder)
    {
        return builder.Services.Any(s => s.ServiceType == typeof(IUserRevalidator));
    }

    [Fact]
    public void WithCredential_RegistersRevalidator_AndDeclaresCp1()
    {
        var builder = Build(new Dictionary<string, string> { ["AzureAd:ClientSecret"] = "s3cr3t" });

        Assert.True(HasRevalidator(builder));
        Assert.Contains("cp1", builder.Configuration.GetSection("AzureAd:ClientCapabilities").GetChildren().Select(c => c.Value));
    }

    [Fact]
    public void ExistingCapabilities_AreKept_AndCp1NotDuplicated()
    {
        var builder = Build(new Dictionary<string, string>
        {
            ["AzureAd:ClientSecret"] = "s3cr3t",
            ["AzureAd:ClientCapabilities:0"] = "other",
            ["AzureAd:ClientCapabilities:1"] = "CP1"
        });

        Assert.Equal(new[] { "other", "CP1" },
            builder.Configuration.GetSection("AzureAd:ClientCapabilities").GetChildren().Select(c => c.Value));
    }

    [Fact]
    public void WithoutCredential_NoRevalidatorOrCp1()
    {
        var builder = Build(new Dictionary<string, string>());

        Assert.False(HasRevalidator(builder));
        Assert.Empty(builder.Configuration.GetSection("AzureAd:ClientCapabilities").GetChildren());
    }

    [Fact]
    public void GraphDisabled_NoRevalidatorOrCp1()
    {
        var builder = Build(new Dictionary<string, string>
        {
            ["AzureAd:ClientSecret"] = "s3cr3t",
            ["Authentication:Entra:EnableGraph"] = "false"
        });

        Assert.False(HasRevalidator(builder));
        Assert.Empty(builder.Configuration.GetSection("AzureAd:ClientCapabilities").GetChildren());
    }
}

public class GraphCredentialTests
{
    private static string Describe(Dictionary<string, string> azureAd)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(azureAd.ToDictionary(kv => "AzureAd:" + kv.Key, kv => kv.Value))
            .Build();
        return EntraAuthExtensions.DescribeGraphCredential(config.GetSection("AzureAd"));
    }

    [Fact]
    public void NoCredential_IsEmpty()
    {
        Assert.Equal("", Describe(new Dictionary<string, string> { ["ClientId"] = "x" }));
    }

    [Fact]
    public void EmptyClientSecret_CountsAsNone()
    {
        Assert.Equal("", Describe(new Dictionary<string, string> { ["ClientSecret"] = "" }));
    }

    [Fact]
    public void ClientSecret_IsDescribedWithoutItsValue()
    {
        string description = Describe(new Dictionary<string, string> { ["ClientSecret"] = "s3cr3t" });

        Assert.Equal("client secret", description);
        Assert.DoesNotContain("s3cr3t", description);
    }

    [Theory]
    [InlineData("StoreWithDistinguishedName")]
    [InlineData("StoreWithThumbprint")]
    [InlineData("Path")]
    [InlineData("Base64Encoded")]
    [InlineData("KeyVault")]
    public void ClientCredentialsCertificate_NamesItsSourceType(string sourceType)
    {
        Assert.Equal("certificate (" + sourceType + ")", Describe(new Dictionary<string, string>
        {
            ["ClientCredentials:0:SourceType"] = sourceType,
            ["ClientCredentials:0:CertificateDistinguishedName"] = "CN=WebJEA-Graph"
        }));
    }

    [Fact]
    public void ClientCredentials_TakesPrecedenceOverClientSecret()
    {
        Assert.Equal("certificate (Path)", Describe(new Dictionary<string, string>
        {
            ["ClientSecret"] = "s3cr3t",
            ["ClientCredentials:0:SourceType"] = "Path",
            ["ClientCredentials:0:CertificateDiskPath"] = "/certs/graph.pfx"
        }));
    }

    [Fact]
    public void ClientCredentialsSecretEntry_IsAClientSecret()
    {
        Assert.Equal("client secret", Describe(new Dictionary<string, string>
        {
            ["ClientCredentials:0:SourceType"] = "ClientSecret",
            ["ClientCredentials:0:ClientSecret"] = "s3cr3t"
        }));
    }

    [Fact]
    public void LegacyClientCertificates_AreListedBeforeClientSecret()
    {
        Assert.Equal("certificate (StoreWithThumbprint), client secret", Describe(new Dictionary<string, string>
        {
            ["ClientSecret"] = "s3cr3t",
            ["ClientCertificates:0:SourceType"] = "StoreWithThumbprint",
            ["ClientCertificates:0:CertificateThumbprint"] = "ABCDEF"
        }));
    }
}

public class ClaimsUserContextTests
{
    [Fact]
    public void AllClaimValues_BecomeMemberOfIds()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, @"DEV\tester"),
            new Claim(ClaimTypes.GroupSid, "S-1-5-21-A"),
            new Claim(ClaimTypes.GroupSid, "S-1-5-21-B")
        }, "Test"));

        var ctx = new ClaimsUserContext(principal);

        Assert.Equal(@"DEV\tester", ctx.UserName);
        Assert.Contains(@"DEV\tester", ctx.MemberOfIds);
        Assert.Contains("S-1-5-21-A", ctx.MemberOfIds);
        Assert.True(ctx.IsMemberOf("S-1-5-21-B"));
        Assert.True(ctx.IsMemberOf("*"));
        Assert.False(ctx.IsMemberOf("S-1-5-21-C"));
    }
}
