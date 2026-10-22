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

    /// <summary>Uses the manifest entry point, which Hub 4.x places in the package app directory.</summary>
    [Fact]
    public void GetPackagedCandidates_ManifestExecutable_ReturnsManifestPathFirst()
    {
        const string root = @"C:\Program Files\WindowsApps\UnityTechnologies.UnityHub_4.0.0.20003_x64__2vrhnee42bhxm";

        var result = UnityHubLocator.GetPackagedCandidates(root, @"app\Unity Hub.exe");

        Assert.Equal(Path.Combine(root, "app", "Unity Hub.exe"), result[0]);
        Assert.Equal(2, result.Count);
    }

    /// <summary>Falls back to the conventional package layout when the manifest cannot be read.</summary>
    [Fact]
    public void GetPackagedCandidates_WithoutManifest_ProbesKnownLayouts()
    {
        const string root = @"C:\Program Files\WindowsApps\UnityTechnologies.UnityHub_4.0.0.20003_x64__2vrhnee42bhxm";

        var result = UnityHubLocator.GetPackagedCandidates(root, null);

        Assert.Equal(2, result.Count);
        Assert.Equal(Path.Combine(root, "app", "Unity Hub.exe"), result[0]);
        Assert.Equal(Path.Combine(root, "Unity Hub.exe"), result[1]);
    }

    /// <summary>Reads the packaged application entry point from an AppxManifest.xml document.</summary>
    [Fact]
    public void ReadManifestExecutable_ValidManifest_ReturnsExecutable()
    {
        var directory = Directory.CreateTempSubdirectory("hubroute-manifest");
        try
        {
            var manifest = Path.Combine(directory.FullName, "AppxManifest.xml");
            File.WriteAllText(
                manifest,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Applications>
                    <Application Id="UnityHub" Executable="app\Unity Hub.exe" EntryPoint="Windows.FullTrustApplication" />
                  </Applications>
                </Package>
                """);

            Assert.Equal(@"app\Unity Hub.exe", UnityHubLocator.ReadManifestExecutable(manifest));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    /// <summary>Returns null for missing or unparsable manifests instead of failing discovery.</summary>
    [Fact]
    public void ReadManifestExecutable_MissingOrMalformed_ReturnsNull()
    {
        var directory = Directory.CreateTempSubdirectory("hubroute-manifest-broken");
        try
        {
            Assert.Null(UnityHubLocator.ReadManifestExecutable(Path.Combine(directory.FullName, "absent.xml")));

            var malformed = Path.Combine(directory.FullName, "AppxManifest.xml");
            File.WriteAllText(malformed, "<Package><Applications>");
            Assert.Null(UnityHubLocator.ReadManifestExecutable(malformed));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    /// <summary>Matches Hub packages by family name or by the display name stored in the registration store.</summary>
    [Theory]
    [InlineData("UnityTechnologies.UnityHub_4.0.0.20003_x64__2vrhnee42bhxm", null, true)]
    [InlineData("UnityTechnologies.UnityHub", null, true)]
    [InlineData("SomeOther.Package", "Unity Hub", true)]
    [InlineData("SomeOther.Package", "Other App", false)]
    [InlineData(null, null, false)]
    public void IsUnityHubPackage_KnownRegistrations_ReturnExpectedResult(
        string? packageName,
        string? displayName,
        bool expected)
    {
        Assert.Equal(expected, UnityHubLocator.IsUnityHubPackage(packageName, displayName));
    }

    /// <summary>Accepts the Hub 4.x app execution alias alongside the 3.x executable name.</summary>
    [Fact]
    public void IsSupportedExecutableName_MatchesCurrentPlatformNames()
    {
        var accepted = OperatingSystem.IsWindows()
            ? new[] { "Unity Hub.exe", "unityhub.exe" }
            : OperatingSystem.IsMacOS()
                ? new[] { "Unity Hub" }
                : ["unityhub", "UnityHub.AppImage"];

        Assert.All(accepted, name => Assert.True(UnityHubLocator.IsSupportedExecutableName(name)));
        Assert.False(UnityHubLocator.IsSupportedExecutableName("notepad.exe"));
    }
}
