using HubRoute.Models;
using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class ProxyDiscoveryServiceTests
{
    /// <summary>Confirms Windows per-protocol settings prefer the HTTP endpoint.</summary>
    [Fact]
    public void ParseWindowsProxyServer_PerProtocolValue_SelectsHttp()
    {
        var result = ProxyDiscoveryService.ParseWindowsProxyServer(
            "http=127.0.0.1:7890;https=127.0.0.1:7891");

        Assert.NotNull(result);
        Assert.Equal("http://127.0.0.1:7890", result.DisplayUrl);
        Assert.Equal("Windows 系统代理", result.Source);
    }

    /// <summary>Confirms a scheme-less proxy receives the documented HTTP scheme.</summary>
    [Fact]
    public void ParseProxyUri_SchemeLessAddress_NormalizesToHttp()
    {
        var result = ProxyDiscoveryService.ParseProxyUri("127.0.0.1:7890");

        Assert.Equal("http", result.Scheme);
        Assert.Equal("127.0.0.1", result.Host);
        Assert.Equal(7890, result.Port);
    }

    /// <summary>Rejects SOCKS endpoints because Unity's documented variables expect HTTP proxies.</summary>
    [Fact]
    public void ParseProxyUri_SocksAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ProxyDiscoveryService.ParseProxyUri("socks5://127.0.0.1:1080"));
    }

    /// <summary>Parses the explicit HTTP proxy fields emitted by macOS scutil.</summary>
    [Fact]
    public void ParseMacOsProxy_EnabledHttpProxy_ReturnsEndpoint()
    {
        const string output = """
                              <dictionary> {
                                HTTPEnable : 1
                                HTTPPort : 7890
                                HTTPProxy : 127.0.0.1
                              }
                              """;

        var result = ProxyDiscoveryService.ParseMacOsProxy(output);

        Assert.NotNull(result);
        Assert.Equal("http://127.0.0.1:7890", result.DisplayUrl);
        Assert.Equal("macOS 系统代理", result.Source);
    }

    /// <summary>Ensures credentials never appear in display-safe proxy text.</summary>
    [Fact]
    public void DisplayUrl_CredentialedProxy_RedactsUserInfo()
    {
        var endpoint = new ProxyEndpoint(
            new Uri("http://user:secret@proxy.example:8080"),
            "test",
            true);

        Assert.Equal("http://proxy.example:8080", endpoint.DisplayUrl);
    }
}
