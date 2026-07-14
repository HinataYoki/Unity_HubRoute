using System.Runtime.InteropServices;
using HubRoute.Models;
using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class UnityHubDownloadServiceTests
{
    /// <summary>Uses Unity's official CDN when the runtime platform has a supported installer.</summary>
    [Fact]
    public void GetCurrentPlatform_DesktopPlatform_ReturnsOfficialInstaller()
    {
        var result = UnityHubDownloadService.GetCurrentPlatform();

        if (OperatingSystem.IsLinux()
            || RuntimeInformation.OSArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal("https", result.DownloadUri.Scheme);
        Assert.Equal("public-cdn.cloud.unity3d.com", result.DownloadUri.Host);
        var expectedArchitecture = RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? "arm64"
            : "x64";
        Assert.Contains(expectedArchitecture, result.FileName, StringComparison.Ordinal);
        Assert.EndsWith(
            OperatingSystem.IsWindows() ? ".exe" : ".dmg",
            result.FileName,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps every supported platform and architecture without consulting the host machine.</summary>
    [Fact]
    public void GetPlatformDownload_AllSupportedCombinations_ReturnExpectedFiles()
    {
        var windowsX64 = UnityHubDownloadService.GetPlatformDownload(
            OSPlatform.Windows,
            Architecture.X64);
        var windowsArm64 = UnityHubDownloadService.GetPlatformDownload(
            OSPlatform.Windows,
            Architecture.Arm64);
        var macX64 = UnityHubDownloadService.GetPlatformDownload(
            OSPlatform.OSX,
            Architecture.X64);
        var macArm64 = UnityHubDownloadService.GetPlatformDownload(
            OSPlatform.OSX,
            Architecture.Arm64);

        Assert.Equal("UnityHubSetup-x64.exe", windowsX64?.FileName);
        Assert.Equal("UnityHubSetup-arm64.exe", windowsArm64?.FileName);
        Assert.Equal("UnityHubSetup-x64.dmg", macX64?.FileName);
        Assert.Equal("UnityHubSetup-arm64.dmg", macArm64?.FileName);
    }

    /// <summary>Declines unsupported systems instead of guessing a compatible installer.</summary>
    [Theory]
    [InlineData("windows", Architecture.X86)]
    [InlineData("windows", Architecture.Arm)]
    [InlineData("linux", Architecture.X64)]
    [InlineData("linux", Architecture.Arm64)]
    public void GetPlatformDownload_UnsupportedCombination_ReturnsNull(
        string platformName,
        Architecture architecture)
    {
        var platform = platformName == "windows"
            ? OSPlatform.Windows
            : OSPlatform.Linux;

        Assert.Null(UnityHubDownloadService.GetPlatformDownload(platform, architecture));
    }

    /// <summary>Separates decoded proxy credentials from the proxy server address.</summary>
    [Fact]
    public void CreateProxy_CredentialedUri_ConfiguresNetworkCredential()
    {
        var proxy = UnityHubDownloadService.CreateProxy(
            new Uri("http://user:p%40ss@proxy.example:8080"));

        Assert.Equal("http://proxy.example:8080/", proxy.Address?.ToString());
        var credential = proxy.Credentials?.GetCredential(proxy.Address!, "Basic");
        Assert.NotNull(credential);
        Assert.Equal("user", credential.UserName);
        Assert.Equal("p@ss", credential.Password);
    }

    /// <summary>Calculates byte progress only when a positive total length is available.</summary>
    [Theory]
    [InlineData(50, 200, 25.0)]
    [InlineData(0, 0, null)]
    public void Percentage_ContentLength_ReturnsExpectedValue(
        long bytesReceived,
        long totalBytes,
        double? expected)
    {
        var progress = new DownloadProgress(bytesReceived, totalBytes);

        Assert.Equal(expected, progress.Percentage);
    }
}
