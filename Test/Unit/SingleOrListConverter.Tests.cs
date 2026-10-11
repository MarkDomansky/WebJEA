using Newtonsoft.Json;
using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class SingleOrListConverterTests
{
    [Fact]
    public void Command_PermittedGroupsAsString_LoadsAsOneElementList()
    {
        var cmd = JsonConvert.DeserializeObject<ConfigCmd>("{ \"Id\": \"a\", \"PermittedGroups\": \"*\" }");

        Assert.Equal(new List<string> { "*" }, cmd.PermittedGroups);
    }

    [Fact]
    public void Command_PermittedGroupsAsArray_LoadsAllEntries()
    {
        var cmd = JsonConvert.DeserializeObject<ConfigCmd>("{ \"Id\": \"a\", \"PermittedGroups\": [ \"g1\", \"g2\" ] }");

        Assert.Equal(new List<string> { "g1", "g2" }, cmd.PermittedGroups);
    }

    [Fact]
    public void Command_PermittedGroupsOmitted_KeepsEmptyDefault()
    {
        var cmd = JsonConvert.DeserializeObject<ConfigCmd>("{ \"Id\": \"a\" }");

        Assert.Empty(cmd.PermittedGroups);
    }

    [Fact]
    public void Command_PermittedGroupsAsNumber_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<ConfigCmd>("{ \"Id\": \"a\", \"PermittedGroups\": 5 }"));
    }

    [Fact]
    public void Config_PermittedGroupsAsString_LoadsAsOneElementList()
    {
        var config = JsonConvert.DeserializeObject<Config>("{ \"Title\": \"t\", \"permittedgroups\": \"Domain Admins\", \"Commands\": [] }");

        Assert.Equal(new List<string> { "Domain Admins" }, config.PermittedGroups);
    }

    [Fact]
    public void Config_PermittedGroupsAsArray_LoadsAllEntries()
    {
        var config = JsonConvert.DeserializeObject<Config>("{ \"Title\": \"t\", \"PermittedGroups\": [ \"g1\", \"g2\" ], \"Commands\": [] }");

        Assert.Equal(new List<string> { "g1", "g2" }, config.PermittedGroups);
    }
}
