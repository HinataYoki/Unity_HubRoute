using System.Runtime.InteropServices;
using HubRoute.Models;

namespace HubRoute.Services;

/// <summary>Maps supported desktop platforms to official Unity Hub installer links.</summary>
public static class UnityHubInstallerCatalog
{
    private const string InstallerBaseUrl = "https://public-cdn.cloud.unity3d.com/hub/prod/";

    /// <summary>Returns the official installer link for the current desktop platform.</summary>
    public static UnityHubInstallerInfo? GetCurrentPlatform()
    {
        var platform = OperatingSystem.IsWindows()
            ? OSPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? OSPlatform.OSX
                : OSPlatform.Linux;
        return GetPlatformInstaller(platform, RuntimeInformation.OSArchitecture);
    }

    /// <summary>Maps a desktop platform and CPU architecture to an official installer link.</summary>
    internal static UnityHubInstallerInfo? GetPlatformInstaller(
        OSPlatform platform,
        Architecture architecture)
    {
        var architectureLabel = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (architectureLabel is null)
        {
            return null;
        }

        if (platform == OSPlatform.Windows)
        {
            return CreateInstaller($"UnityHubSetup-{architectureLabel}.exe", "Windows");
        }

        return platform == OSPlatform.OSX
            ? CreateInstaller($"UnityHubSetup-{architectureLabel}.dmg", "macOS")
            : null;
    }

    /// <summary>Builds one immutable installer description from a CDN filename and platform label.</summary>
    private static UnityHubInstallerInfo CreateInstaller(string fileName, string platformLabel)
    {
        return new UnityHubInstallerInfo(
            new Uri(InstallerBaseUrl + fileName),
            fileName,
            platformLabel);
    }
}
