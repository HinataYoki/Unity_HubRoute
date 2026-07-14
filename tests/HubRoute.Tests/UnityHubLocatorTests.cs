using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class UnityHubLocatorTests
{
    /// <summary>Converts a macOS app bundle into its executable path.</summary>
    [Fact]
    public void ResolveSelectedPath_AppBundle_ReturnsInnerExecutable()
    {
        var result = UnityHubLocator.ResolveSelectedPath("/Applications/Unity Hub.app");

        Assert.Equal(
            Path.Combine("/Applications/Unity Hub.app", "Contents", "MacOS", "Unity Hub"),
            result);
    }

    /// <summary>Preserves a normal executable path without platform mutation.</summary>
    [Fact]
    public void ResolveSelectedPath_Executable_ReturnsOriginalPath()
    {
        const string path = "/usr/bin/unityhub";

        Assert.Equal(path, UnityHubLocator.ResolveSelectedPath(path));
    }

    /// <summary>Builds candidates from both Windows installation metadata values.</summary>
    [Fact]
    public void GetRegistryCandidates_InstallAndIconValues_ReturnsBothPaths()
    {
        var result = UnityHubLocator.GetRegistryCandidates(
            @"D:\Unity\Hub\Unity Hub",
            "\"D:\\Unity\\Hub\\Unity Hub\\Unity Hub.exe\",0");

        Assert.Equal(2, result.Count);
        Assert.Equal(
            Path.Combine(@"D:\Unity\Hub\Unity Hub", "Unity Hub.exe"),
            result[0]);
        Assert.Equal(@"D:\Unity\Hub\Unity Hub\Unity Hub.exe", result[1]);
    }
}
