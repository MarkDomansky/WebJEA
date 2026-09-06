using WebJEA;
using WebJEA.Hosting;
using Xunit;

namespace WebJEA.Tests.Hosting;

public class KestrelEndpointsTests
{
    [Fact]
    public void IsConfigured_IsFalse_WhenNoPortsSet()
    {
        Assert.False(KestrelEndpoints.IsConfigured(new WebJeaOptions()));
    }

    [Theory]
    [InlineData(80, null)]
    [InlineData(null, 443)]
    [InlineData(80, 443)]
    public void IsConfigured_IsTrue_WhenAnyPortSet(int? http, int? https)
    {
        var options = new WebJeaOptions { HttpPort = http, HttpsPort = https, CertThumbprint = "AB12" };
        Assert.True(KestrelEndpoints.IsConfigured(options));
    }

    [Fact]
    public void IsConfigured_IsFalse_WhenPortsAreZero()
    {
        // Deploy.ps1 writes 0 to mean "listener disabled"; treat it like absent.
        Assert.False(KestrelEndpoints.IsConfigured(new WebJeaOptions { HttpPort = 0, HttpsPort = 0 }));
    }

    [Fact]
    public void Validate_Throws_WhenNoPortsEnabled()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => KestrelEndpoints.Validate(new WebJeaOptions()));
        Assert.Contains("HttpPort", ex.Message);
        Assert.Contains("HttpsPort", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenHttpsEnabledWithoutThumbprint()
    {
        var options = new WebJeaOptions { HttpsPort = 443 };
        var ex = Assert.Throws<InvalidOperationException>(() => KestrelEndpoints.Validate(options));
        Assert.Contains("CertThumbprint", ex.Message);
    }

    [Fact]
    public void Validate_HttpOnly_DoesNotThrow()
    {
        KestrelEndpoints.Validate(new WebJeaOptions { HttpPort = 80 });
    }

    [Fact]
    public void Validate_HttpsOnly_WithThumbprint_DoesNotThrow()
    {
        var options = new WebJeaOptions { HttpsPort = 443, CertThumbprint = "AB12" };
        KestrelEndpoints.Validate(options);
    }

    [Fact]
    public void LoadCertificate_Throws_WithHelpfulMessage_WhenNotFound()
    {
        // 40 hex chars that will never match a real cert.
        const string thumbprint = "0000000000000000000000000000000000000000";
        var ex = Assert.Throws<InvalidOperationException>(
            () => KestrelEndpoints.LoadCertificate(thumbprint));
        Assert.Contains(thumbprint, ex.Message);
        Assert.Contains("LocalMachine", ex.Message);
    }

    [Theory]
    [InlineData(80, 443, "AB12", true)]
    [InlineData(null, 443, "AB12", false)]
    [InlineData(0, 443, "AB12", false)]
    [InlineData(80, null, "AB12", false)]
    [InlineData(80, 0, "AB12", false)]
    [InlineData(80, 443, null, false)]
    [InlineData(80, 443, "", false)]
    public void ShouldRedirectHttpToHttps_RequiresAllThreeSettings(
        int? httpPort, int? httpsPort, string certThumbprint, bool expected)
    {
        var options = new WebJeaOptions
        {
            HttpPort = httpPort,
            HttpsPort = httpsPort,
            CertThumbprint = certThumbprint
        };
        Assert.Equal(expected, options.ShouldRedirectHttpToHttps);
    }
}
