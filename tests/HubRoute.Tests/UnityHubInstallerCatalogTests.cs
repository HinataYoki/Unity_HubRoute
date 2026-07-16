using System.Runtime.InteropServices;
using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class UnityHubInstallerCatalogTests
{
    /// <summary>Uses Unity's official CDN when the runtime platform has a supported installer.</summary>
    [Fact]
    public void GetCurrentPlatform_DesktopPlatform_ReturnsOfficialInstaller()
    {
        var result = UnityHubInstallerCatalog.GetCurrentPlatform();

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
    public void GetPlatformInstaller_AllSupportedCombinations_ReturnExpectedFiles()
    {
        var windowsX64 = UnityHubInstallerCatalog.GetPlatformInstaller(
            OSPlatform.Windows,
            Architecture.X64);
        var windowsArm64 = UnityHubInstallerCatalog.GetPlatformInstaller(
            OSPlatform.Windows,
            Architecture.Arm64);
        var macX64 = UnityHubInstallerCatalog.GetPlatformInstaller(
            OSPlatform.OSX,
            Architecture.X64);
        var macArm64 = UnityHubInstallerCatalog.GetPlatformInstaller(
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
    public void GetPlatformInstaller_UnsupportedCombination_ReturnsNull(
        string platformName,
        Architecture architecture)
    {
        var platform = platformName == "windows"
            ? OSPlatform.Windows
            : OSPlatform.Linux;

        Assert.Null(UnityHubInstallerCatalog.GetPlatformInstaller(platform, architecture));
    }
}
