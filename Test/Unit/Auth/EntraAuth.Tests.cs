using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
