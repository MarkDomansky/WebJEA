using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class PSCmdTests
{
    #region Default property values

    [Fact]
    public void NewPSCmd_Parameters_IsEmptyList()
    {
        var cmd = new PSCmd();

        Assert.NotNull(cmd.Parameters);
        Assert.Empty(cmd.Parameters);
    }

    #endregion

    #region Init - path resolution

    [Fact]
    public void Init_AbsoluteScriptPath_RemainsAbsolute()
    {
        var cmd = new PSCmd();
        cmd.Script = @"C:\scripts\test.ps1";

        cmd.Init(@"C:\base", true);

        Assert.Equal(@"C:\scripts\test.ps1", cmd.Script);
    }

    [Fact]
    public void Init_RelativeScriptPath_PrependBasePath()
    {
        var cmd = new PSCmd();
        cmd.Script = "test.ps1";

        cmd.Init(@"C:\base", true);

        Assert.Equal(@"C:\base\test.ps1", cmd.Script);
    }

    [Fact]
    public void Init_RootedScriptPath_PrependBasePath()
    {
        var cmd = new PSCmd();
        cmd.Script = @"\scripts\test.ps1";

        cmd.Init(@"C:\base", true);

        Assert.Equal(@"C:\base\scripts\test.ps1", cmd.Script);
    }

    [Fact]
    public void Init_EmptyScript_NoChange()
    {
        var cmd = new PSCmd();
        cmd.Script = "";

        cmd.Init(@"C:\base", true);

        Assert.Equal("", cmd.Script);
    }

    [Fact]
    public void Init_LogParamsDefault_InheritsFromParam()
    {
        var cmd = new PSCmd();
        cmd.Script = "";

        cmd.Init(@"C:\base", true);

        Assert.True(cmd.LogParameters);
    }

    [Fact]
    public void Init_LogParamsDefault_InheritsFalse()
    {
        var cmd = new PSCmd();
        cmd.Script = "";

        cmd.Init(@"C:\base", false);

        Assert.False(cmd.LogParameters);
    }

    #endregion

    #region Init - script parsing with real scripts

    private readonly string TestScriptsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts");

    [Fact]
    public void Init_WithParsing_PopulatesParsedSynopsis()
    {
        var cmd = new PSCmd();
        var scriptPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "ValidScript.ps1"));
        cmd.Script = scriptPath;

        cmd.Init("", true);

        Assert.Equal("This is a valid script.", cmd.ParsedSynopsis);
    }

    [Fact]
    public void Init_WithParsing_MergesParameters()
    {
        var cmd = new PSCmd();
        var scriptPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "ValidScript.ps1"));
        cmd.Script = scriptPath;

        cmd.Init("", true);

        Assert.True(cmd.Parameters.Count >= 2);
    }

    #endregion
}
