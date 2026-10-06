using Xunit;
using Moq;
using WebJEA;

namespace WebJEA.Tests;

public class CommandServiceTests
{
    private readonly string TestScriptsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts");

    #region LoadConfig - JSON deserialization

    [Fact]
    public void LoadConfig_ValidConfigFile_LoadsTitle()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        Assert.NotNull(svc.Config);
        Assert.NotNull(svc.Config.Title);
    }

    [Fact]
    public void LoadConfig_ValidConfigFile_LoadsCommands()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        Assert.NotNull(svc.Config.Commands);
        Assert.NotEmpty(svc.Config.Commands);
    }

    [Fact]
    public void LoadConfig_InvalidFilePath_Throws()
    {
        var svc = new CommandService();
        var mockResolver = new Mock<IGroupResolver>();

        Assert.Throws<Exception>(() => svc.LoadConfig(@"C:\nonexistent\bad.json", mockResolver.Object));
    }

    [Fact]
    public void LoadConfig_InitializesAuthService()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        Assert.NotNull(svc.Auth);
    }

    [Fact]
    public void LoadConfig_CommandWithDescription_LoadsAndIgnoresIt()
    {
        // Description is not a config setting: the schema tolerates it as an unknown
        // property, and the app must load the file without using the value anywhere.
        string scriptsPath = Path.GetFullPath(TestScriptsPath);
        string configPath = Path.Combine(Path.GetTempPath(), "webjea-description-" + Guid.NewGuid().ToString("N") + ".json");
        var config = new Newtonsoft.Json.Linq.JObject
        {
            ["Title"] = "t",
            ["BasePath"] = scriptsPath,
            ["Commands"] = new Newtonsoft.Json.Linq.JArray
            {
                new Newtonsoft.Json.Linq.JObject
                {
                    ["Id"] = "described",
                    ["Description"] = "Description from config",
                    ["Script"] = "utsp-help-description.ps1",
                    ["PermittedGroups"] = "*"
                }
            }
        };
        File.WriteAllText(configPath, config.ToString());
        try
        {
            var svc = new CommandService();
            var mockResolver = new Mock<IGroupResolver>();
            mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

            svc.LoadConfig(configPath, mockResolver.Object);

            ConfigCmd cmd = Assert.Single(svc.Config.Commands);
            Assert.Equal("described", cmd.ID);
            Assert.Null(cmd.GetMenuItem().Description);

            PSCmd scriptCmd = svc.GetScriptCmd("described");
            var metadata = new FormMetadataBuilder().Build(cmd, scriptCmd, null, svc.Config.Title, false);
            Assert.Contains("Description String Check", metadata.Description);
            Assert.DoesNotContain("Description from config", metadata.Description);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void LoadConfig_ConfiguredFileMissing_FallsBackToLegacyConfigJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "webjea-legacyconfig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json")), Path.Combine(dir, CommandService.LegacyConfigFileName));
            var svc = new CommandService();
            var mockResolver = new Mock<IGroupResolver>();
            mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

            svc.LoadConfig(Path.Combine(dir, "webjea.json"), mockResolver.Object);

            Assert.NotNull(svc.Config);
            Assert.NotEmpty(svc.Config.Commands);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    #endregion

    #region ResolveCommandId

    [Fact]
    public void ResolveCommandId_AuthorizedUser_ReturnsRequestedId()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID("*")).Returns("*");
        mockResolver.Setup(g => g.GetSID(It.IsNotIn("*"))).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var user = new TestUserContext(new List<string> { "S-1-5-21-DUMMY" }, @"DOMAIN\user");

        var result = svc.ResolveCommandId(user, svc.Config.Commands[0].ID);
        Assert.Equal(svc.Config.Commands[0].ID, result);
    }

    [Fact]
    public void ResolveCommandId_UnauthorizedUser_ReturnsEmptyString()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var user = new TestUserContext(new List<string> { "S-1-5-21-NOBODY" }, @"DOMAIN\nobody");

        var result = svc.ResolveCommandId(user, "nonexistent-cmd");
        Assert.Equal("", result);
    }

    #endregion

    #region GetCommand

    [Fact]
    public void GetCommand_AuthorizedUser_ReturnsConfigCmd()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("S-1-5-21-MATCH");

        svc.LoadConfig(configPath, mockResolver.Object);

        var user = new TestUserContext(new List<string> { "S-1-5-21-MATCH" }, @"DOMAIN\user");

        var cmd = svc.GetCommand(user, svc.Config.Commands[0].ID);
        Assert.NotNull(cmd);
        Assert.IsType<ConfigCmd>(cmd);
    }

    [Fact]
    public void GetCommand_UnauthorizedUser_ReturnsNothing()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var user = new TestUserContext(new List<string> { "S-1-5-21-NOBODY" }, @"DOMAIN\nobody");

        var firstCmdId = svc.Config.Commands[0].ID;
        var cmd = svc.GetCommand(user, firstCmdId);
        Assert.Null(cmd);
    }

    #endregion

    #region GetScriptCmd

    [Fact]
    public void GetScriptCmd_CommandWithScript_ReturnsNonNull()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var scriptCmd = svc.GetScriptCmd("validate");

        Assert.NotNull(scriptCmd);
        Assert.False(string.IsNullOrEmpty(scriptCmd.Script));
    }

    [Fact]
    public void GetScriptCmd_CommandWithoutScript_ReturnsNothing()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var scriptCmd = svc.GetScriptCmd("t3");

        Assert.Null(scriptCmd);
    }

    [Fact]
    public void GetScriptCmd_UnknownCommand_ReturnsNothing()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var scriptCmd = svc.GetScriptCmd("nonexistent-cmd");

        Assert.Null(scriptCmd);
    }

    [Fact]
    public void GetScriptCmd_CalledTwice_ReturnsSameInstance()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var first = svc.GetScriptCmd("validate");
        var second = svc.GetScriptCmd("validate");

        Assert.Same(first, second);
    }

    #endregion

    #region GetOnloadCmd

    [Fact]
    public void GetOnloadCmd_CommandWithOnloadScript_ReturnsNonNull()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var onloadCmd = svc.GetOnloadCmd("validate");

        Assert.NotNull(onloadCmd);
        Assert.False(string.IsNullOrEmpty(onloadCmd.Script));
    }

    [Fact]
    public void GetOnloadCmd_CommandWithoutOnloadScript_ReturnsNothing()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var onloadCmd = svc.GetOnloadCmd("t0");

        Assert.Null(onloadCmd);
    }

    [Fact]
    public void GetOnloadCmd_ScriptIsOnloadScriptPath()
    {
        var svc = new CommandService();
        var configPath = Path.GetFullPath(Path.Combine(TestScriptsPath, "webjea.json"));
        var mockResolver = new Mock<IGroupResolver>();
        mockResolver.Setup(g => g.GetSID(It.IsAny<string>())).Returns("");

        svc.LoadConfig(configPath, mockResolver.Object);

        var onloadCmd = svc.GetOnloadCmd("validate");

        Assert.NotNull(onloadCmd);
        Assert.Contains("validate-onload", onloadCmd.Script);
    }

    #endregion
}
