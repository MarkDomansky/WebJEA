using Microsoft.Extensions.Configuration;
using WebJEA.Hosting;
using Xunit;

namespace WebJEA.Tests.Hosting;

public class KestrelEndpointsTests
{
    private static IConfiguration Config(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void RejectLegacySettings_DoesNotThrow_WhenNoLegacyKeys()
    {
        KestrelEndpoints.RejectLegacySettings(Config(new Dictionary<string, string>
        {
            ["WebJEA:ConfigFile"] = @"c:\webjea\scripts\webjea.json",
            ["Kestrel:Endpoints:Https:Url"] = "https://*:443"
        }));
    }

    [Theory]
    [InlineData("WebJEA:HttpPort", "80")]
    [InlineData("WebJEA:HttpsPort", "443")]
    [InlineData("WebJEA:CertThumbprint", "AB12")]
    // Deploy.ps1 wrote 0 / "" to mean "disabled"; the key is still retired.
    [InlineData("WebJEA:HttpPort", "0")]
    [InlineData("WebJEA:CertThumbprint", "")]
    public void RejectLegacySettings_Throws_NamingTheKey(string key, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => KestrelEndpoints.RejectLegacySettings(Config(new Dictionary<string, string> { [key] = value })));
        Assert.Contains(key, ex.Message);
        Assert.Contains("Kestrel:Endpoints", ex.Message);
        Assert.Contains("Deploy.ps1", ex.Message);
    }
}
