using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class MenuItemTests
{
    [Fact]
    public void Uri_ReturnsQueryStringWithId()
    {
        var mi = new MenuItem();
        mi.ID = "test-cmd";

        Assert.Equal("command.html?cmdid=test-cmd", mi.Uri());
    }

    [Fact]
    public void Uri_EmptyId_ReturnsQueryStringWithEmptyValue()
    {
        var mi = new MenuItem();
        mi.ID = "";

        Assert.Equal("command.html?cmdid=", mi.Uri());
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        var mi = new MenuItem();
        mi.ID = "my-id";
        mi.DisplayName = "My Display Name";
        mi.Description = "My Description";

        Assert.Equal("my-id", mi.ID);
        Assert.Equal("My Display Name", mi.DisplayName);
        Assert.Equal("My Description", mi.Description);
    }

    [Fact]
    public void Uri_SpecialCharactersInId_AreNotEncoded()
    {
        var mi = new MenuItem();
        mi.ID = "cmd with spaces";

        Assert.Equal("command.html?cmdid=cmd with spaces", mi.Uri());
    }
}
