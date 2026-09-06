using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class ConfigTests
{
    #region Default property values

    [Fact]
    public void NewConfig_LogParameters_DefaultTrue()
    {
        var config = new Config();
        Assert.True(config.LogParameters);
    }

    [Fact]
    public void NewConfig_SendTelemetry_DefaultTrue()
    {
        var config = new Config();
        Assert.True(config.SendTelemetry);
    }

    [Fact]
    public void NewConfig_HtmlLanguage_DefaultEnUS()
    {
        var config = new Config();
        Assert.Equal("en-US", config.HtmlLanguage);
    }

    [Fact]
    public void NewConfig_ShowVerbose_DefaultTrue()
    {
        var config = new Config();
        Assert.True(config.ShowVerbose);
    }

    [Fact]
    public void NewConfig_PermittedGroups_IsEmptyList()
    {
        var config = new Config();
        Assert.NotNull(config.PermittedGroups);
        Assert.Empty(config.PermittedGroups);
    }

    #endregion

    #region IConfigProvider implementation

    [Fact]
    public void Config_ImplementsIConfigProvider()
    {
        var config = new Config();
        Assert.IsAssignableFrom<IConfigProvider>(config);
    }

    [Fact]
    public void Config_PropertiesRoundTrip()
    {
        var config = new Config();
        config.Title = "Test Title";
        config.LogParameters = false;
        config.BasePath = @"C:\test";
        config.SendTelemetry = false;
        config.HtmlLanguage = "fr-FR";
        config.ShowVerbose = false;

        Assert.Equal("Test Title", config.Title);
        Assert.False(config.LogParameters);
        Assert.Equal(@"C:\test", config.BasePath);
        Assert.False(config.SendTelemetry);
        Assert.Equal("fr-FR", config.HtmlLanguage);
        Assert.False(config.ShowVerbose);
    }

    #endregion
}
