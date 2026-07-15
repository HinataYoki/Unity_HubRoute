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

    /// <summary>Recognizes Hub executables and Electron helpers without matching Unity Editor processes.</summary>
    [Theory]
    [InlineData("Unity Hub", true)]
    [InlineData("UnityHub", true)]
    [InlineData("unityhub", true)]
    [InlineData("Unity Hub Helper", true)]
    [InlineData("Unity Hub Helper (Renderer)", true)]
    [InlineData("Unity", false)]
    [InlineData("UnityPackageManager", false)]
    [InlineData("Unity Hub Project", false)]
    public void IsUnityHubProcessName_KnownNames_ReturnExpectedResult(
        string processName,
        bool expected)
    {
        var result = HubLauncher.IsUnityHubProcessName(
            processName,
            Path.Combine(Path.GetTempPath(), "Unity Hub.exe"));

        Assert.Equal(expected, result);
    }

    /// <summary>Recognizes only the Unity Editor executable and excludes related support processes.</summary>
    [Theory]
    [InlineData("Unity", true)]
    [InlineData("unity", true)]
    [InlineData("Unity.exe", false)]
    [InlineData("Unity Hub", false)]
    [InlineData("UnityPackageManager", false)]
    [InlineData("UnityCrashHandler64", false)]
    public void IsUnityEditorProcessName_KnownNames_ReturnExpectedResult(
        string processName,
        bool expected)
    {
        Assert.Equal(expected, HubLauncher.IsUnityEditorProcessName(processName));
    }
}
