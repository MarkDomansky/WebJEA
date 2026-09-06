using Xunit;
using Moq;
using WebJEA;

namespace WebJEA.Tests;

public class AuthorizationServiceTests
{
    private Mock<IConfigProvider> CreateConfig(List<string> permittedGroups, List<ConfigCmd> commands)
    {
        var mockConfig = new Mock<IConfigProvider>();
        mockConfig.Setup(c => c.PermittedGroups).Returns(permittedGroups);
        mockConfig.Setup(c => c.Commands).Returns(commands);
        return mockConfig;
    }

    private Mock<IGroupResolver> CreateGroupResolver(Dictionary<string, string> mappings)
    {
        var mockResolver = new Mock<IGroupResolver>();
        foreach (var kvp in mappings)
        {
            mockResolver.Setup(g => g.GetSID(kvp.Key)).Returns(kvp.Value);
        }

        return mockResolver;
    }

    private IUserContext CreateUserInfo(List<string> sids)
    {
        return new TestUserContext(sids, @"TESTDOMAIN\testuser");
    }

    private AuthorizationService BuildService(IConfigProvider config, IGroupResolver resolver)
    {
        var svc = new AuthorizationService();
        svc.InitGroups(config, resolver);
        return svc;
    }

    #region IsGlobalUser

    [Fact]
    public void IsGlobalUser_UserInGlobalGroup_ReturnsTrue()
    {
        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Admins", "S-1-5-21-ADMIN" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        Assert.True(svc.IsGlobalUser(user));
    }

    [Fact]
    public void IsGlobalUser_UserNotInGlobalGroup_ReturnsFalse()
    {
        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Admins", "S-1-5-21-ADMIN" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-OTHER" });

        Assert.False(svc.IsGlobalUser(user));
    }

    [Fact]
    public void IsGlobalUser_NoGlobalGroups_ReturnsFalse()
    {
        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(new Dictionary<string, string>());
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        Assert.False(svc.IsGlobalUser(user));
    }

    [Fact]
    public void IsGlobalUser_UserHasMultipleSIDs_MatchesAny()
    {
        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Admins", "S-1-5-21-ADMIN" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-OTHER", "S-1-5-21-ADMIN" });

        Assert.True(svc.IsGlobalUser(user));
    }

    [Fact]
    public void IsGlobalUser_GroupResolverReturnsEmpty_ReturnsFalse()
    {
        var config = CreateConfig(
            new List<string> { "BadGroup" },
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "BadGroup", "" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        Assert.False(svc.IsGlobalUser(user));
    }

    #endregion

    #region IsCommandAvailable

    [Fact]
    public void IsCommandAvailable_GlobalUser_AlwaysTrue()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.PermittedGroups = new List<string> { "SpecificGroup" };

        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string>
            {
                { "Admins", "S-1-5-21-ADMIN" },
                { "SpecificGroup", "S-1-5-21-SPECIFIC" }
            });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        Assert.True(svc.IsCommandAvailable(user, "cmd1"));
    }

    [Fact]
    public void IsCommandAvailable_UserInCommandGroup_ReturnsTrue()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.PermittedGroups = new List<string> { "CommandGroup" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "CommandGroup", "S-1-5-21-CMDGRP" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-CMDGRP" });

        Assert.True(svc.IsCommandAvailable(user, "cmd1"));
    }

    [Fact]
    public void IsCommandAvailable_UserNotInCommandGroup_ReturnsFalse()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.PermittedGroups = new List<string> { "CommandGroup" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "CommandGroup", "S-1-5-21-CMDGRP" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-OTHER" });

        Assert.False(svc.IsCommandAvailable(user, "cmd1"));
    }

    [Fact]
    public void IsCommandAvailable_WildcardGroup_ReturnsTrue()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.PermittedGroups = new List<string> { "*" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "*", "*" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ANYONE" });

        Assert.True(svc.IsCommandAvailable(user, "cmd1"));
    }

    [Fact]
    public void IsCommandAvailable_UnknownCommand_ReturnsFalse()
    {
        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(new Dictionary<string, string>());
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-USER" });

        Assert.False(svc.IsCommandAvailable(user, "nonexistent"));
    }

    #endregion

    #region GetCommand

    [Fact]
    public void GetCommand_AuthorizedUser_ReturnsCommand()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.DisplayName = "Test Command";
        cmd.PermittedGroups = new List<string> { "Users" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Users", "S-1-5-21-USER" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-USER" });

        var result = svc.GetCommand(user, "cmd1");

        Assert.NotNull(result);
        Assert.Equal("cmd1", result.ID);
    }

    [Fact]
    public void GetCommand_UnauthorizedUser_ReturnsNothing()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.PermittedGroups = new List<string> { "PrivilegedGroup" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "PrivilegedGroup", "S-1-5-21-PRIV" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-OTHER" });

        var result = svc.GetCommand(user, "cmd1");

        Assert.Null(result);
    }

    [Fact]
    public void GetCommand_NonexistentCommand_ReturnsNothing()
    {
        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd>());
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Admins", "S-1-5-21-ADMIN" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        var result = svc.GetCommand(user, "nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetCommand_GlobalUser_ReturnsAnyCommand()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "restricted-cmd";
        cmd.PermittedGroups = new List<string> { "SpecificGroup" };

        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string>
            {
                { "Admins", "S-1-5-21-ADMIN" },
                { "SpecificGroup", "S-1-5-21-SPECIFIC" }
            });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        var result = svc.GetCommand(user, "restricted-cmd");

        Assert.NotNull(result);
    }

    #endregion

    #region GetMenu

    [Fact]
    public void GetMenu_GlobalUser_ReturnsAllCommands()
    {
        var cmd1 = new ConfigCmd();
        cmd1.ID = "cmd1";
        cmd1.DisplayName = "Command 1";
        cmd1.PermittedGroups = new List<string> { "Group1" };

        var cmd2 = new ConfigCmd();
        cmd2.ID = "cmd2";
        cmd2.DisplayName = "Command 2";
        cmd2.PermittedGroups = new List<string> { "Group2" };

        var config = CreateConfig(
            new List<string> { "Admins" },
            new List<ConfigCmd> { cmd1, cmd2 });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string>
            {
                { "Admins", "S-1-5-21-ADMIN" },
                { "Group1", "S-1-5-21-G1" },
                { "Group2", "S-1-5-21-G2" }
            });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ADMIN" });

        var menu = svc.GetMenu(user);

        Assert.Equal(2, menu.Count);
    }

    [Fact]
    public void GetMenu_LimitedUser_ReturnsOnlyAuthorizedCommands()
    {
        var cmd1 = new ConfigCmd();
        cmd1.ID = "cmd1";
        cmd1.DisplayName = "Command 1";
        cmd1.PermittedGroups = new List<string> { "AllUsers" };

        var cmd2 = new ConfigCmd();
        cmd2.ID = "cmd2";
        cmd2.DisplayName = "Command 2";
        cmd2.PermittedGroups = new List<string> { "AdminOnly" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd1, cmd2 });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string>
            {
                { "AllUsers", "S-1-5-21-ALL" },
                { "AdminOnly", "S-1-5-21-ADMONLY" }
            });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-ALL" });

        var menu = svc.GetMenu(user);

        Assert.Single(menu);
        Assert.Equal("cmd1", menu[0].ID);
    }

    [Fact]
    public void GetMenu_NoAccess_ReturnsEmptyList()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.PermittedGroups = new List<string> { "SpecialGroup" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "SpecialGroup", "S-1-5-21-SPEC" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-NOBODY" });

        var menu = svc.GetMenu(user);

        Assert.Empty(menu);
    }

    #endregion

    #region GetMenuDataTable

#pragma warning disable CS0618 // GetMenuDataTable is retained (obsolete) for behavioral parity

    [Fact]
    public void GetMenuDataTable_ReturnsCorrectColumns()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.DisplayName = "Command 1";
        cmd.Description = "Description 1";
        cmd.PermittedGroups = new List<string> { "Users" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Users", "S-1-5-21-USER" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-USER" });

        var dt = svc.GetMenuDataTable(user, "");

        Assert.True(dt.Columns.Contains("DisplayName"));
        Assert.True(dt.Columns.Contains("Description"));
        Assert.True(dt.Columns.Contains("Uri"));
        Assert.True(dt.Columns.Contains("CSS"));
    }

    [Fact]
    public void GetMenuDataTable_ActiveCommand_HasActiveCss()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.DisplayName = "Command 1";
        cmd.Description = "Description 1";
        cmd.PermittedGroups = new List<string> { "Users" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Users", "S-1-5-21-USER" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-USER" });

        var dt = svc.GetMenuDataTable(user, "cmd1");

        Assert.Equal(1, dt.Rows.Count);
        Assert.Equal("active", dt.Rows[0]["CSS"].ToString());
    }

    [Fact]
    public void GetMenuDataTable_InactiveCommand_HasEmptyCss()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.DisplayName = "Command 1";
        cmd.Description = "Description 1";
        cmd.PermittedGroups = new List<string> { "Users" };

        var config = CreateConfig(
            new List<string>(),
            new List<ConfigCmd> { cmd });
        var resolver = CreateGroupResolver(
            new Dictionary<string, string> { { "Users", "S-1-5-21-USER" } });
        var svc = BuildService(config.Object, resolver.Object);

        var user = CreateUserInfo(new List<string> { "S-1-5-21-USER" });

        var dt = svc.GetMenuDataTable(user, "other-cmd");

        Assert.Equal(1, dt.Rows.Count);
        Assert.Equal("", dt.Rows[0]["CSS"].ToString());
    }

#pragma warning restore CS0618

    #endregion
}
