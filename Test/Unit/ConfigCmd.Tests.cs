using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class ConfigCmdTests
{
    #region ID property - lowercasing

    [Fact]
    public void ID_SetUpperCase_StoredAsLowerCase()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "MY-COMMAND";

        Assert.Equal("my-command", cmd.ID);
    }

    [Fact]
    public void ID_SetMixedCase_StoredAsLowerCase()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "Get-Process";

        Assert.Equal("get-process", cmd.ID);
    }

    [Fact]
    public void ID_SetLowerCase_UnchangedAsLowerCase()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "already-lower";

        Assert.Equal("already-lower", cmd.ID);
    }

    #endregion

    #region GetMenuItem

    [Fact]
    public void GetMenuItem_ReturnsMenuItemWithCorrectId()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "test-cmd";
        cmd.DisplayName = "Test Command";
        cmd.Description = "A test command";

        var mi = cmd.GetMenuItem();

        Assert.Equal("test-cmd", mi.ID);
        Assert.Equal("Test Command", mi.DisplayName);
        Assert.Equal("A test command", mi.Description);
    }

    [Fact]
    public void GetMenuItem_NullDisplayName_UsesIdAsDisplayName()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "fallback-cmd";
        cmd.DisplayName = null;

        var mi = cmd.GetMenuItem();

        Assert.Equal("fallback-cmd", mi.DisplayName);
    }

    [Fact]
    public void GetMenuItem_HasDisplayName_UsesDisplayName()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.DisplayName = "Friendly Name";

        var mi = cmd.GetMenuItem();

        Assert.Equal("Friendly Name", mi.DisplayName);
    }

    [Fact]
    public void GetMenuItem_Synopsis_PropagatedToMenuItem()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "cmd1";
        cmd.Synopsis = "Short description";

        var mi = cmd.GetMenuItem();

        Assert.Equal("Short description", mi.Synopsis);
    }

    #endregion

    #region Default property values

    [Fact]
    public void NewConfigCmd_PermittedGroups_IsEmptyList()
    {
        var cmd = new ConfigCmd();

        Assert.NotNull(cmd.PermittedGroups);
        Assert.Empty(cmd.PermittedGroups);
    }

    [Fact]
    public void NewConfigCmd_LogParameters_UsesDefault()
    {
        var cmd = new ConfigCmd();

        Assert.Null(cmd.LogParameters);
    }

    #endregion

    #region Property round-trip

    [Fact]
    public void Properties_RoundTrip()
    {
        var cmd = new ConfigCmd();
        cmd.ID = "rt-cmd";
        cmd.DisplayName = "Round Trip";
        cmd.Synopsis = "Synopsis text";
        cmd.Description = "Description text";
        cmd.Script = "script.ps1";
        cmd.OnloadScript = "onload.ps1";
        cmd.LogParameters = true;
        cmd.PermittedGroups.Add("Domain Admins");

        Assert.Equal("rt-cmd", cmd.ID);
        Assert.Equal("Round Trip", cmd.DisplayName);
        Assert.Equal("Synopsis text", cmd.Synopsis);
        Assert.Equal("Description text", cmd.Description);
        Assert.Equal("script.ps1", cmd.Script);
        Assert.Equal("onload.ps1", cmd.OnloadScript);
        Assert.True(cmd.LogParameters);
        Assert.Contains("Domain Admins", cmd.PermittedGroups);
    }

    #endregion
}
