using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class HubLauncherTests
{
    /// <summary>Sets upper and lower case proxy variables for cross-platform child processes.</summary>
    [Fact]
    public void BuildStartInfo_WithProxy_SetsProcessScopedVariables()
    {
        var result = HubLauncher.BuildStartInfo(
            Path.Combine(Path.GetTempPath(), "Unity Hub.exe"),
            new Uri("http://127.0.0.1:7890"));

        Assert.Equal("http://127.0.0.1:7890", result.Environment["HTTP_PROXY"]);
        Assert.Equal("http://127.0.0.1:7890", result.Environment["HTTPS_PROXY"]);
        Assert.Equal("http://127.0.0.1:7890", result.Environment["http_proxy"]);
        Assert.Equal("http://127.0.0.1:7890", result.Environment["https_proxy"]);
        Assert.False(result.UseShellExecute);
    }

    /// <summary>Removes inherited proxy variables when direct mode is selected.</summary>
    [Fact]
    public void BuildStartInfo_WithoutProxy_RemovesInheritedVariables()
    {
        var result = HubLauncher.BuildStartInfo(
            Path.Combine(Path.GetTempPath(), "Unity Hub.exe"),
            null);

        Assert.DoesNotContain("HTTP_PROXY", result.Environment.Keys);
        Assert.DoesNotContain("HTTPS_PROXY", result.Environment.Keys);
        Assert.DoesNotContain("http_proxy", result.Environment.Keys);
        Assert.DoesNotContain("https_proxy", result.Environment.Keys);
    }
}
