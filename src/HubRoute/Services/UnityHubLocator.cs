using HubRoute.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace HubRoute.Services;

/// <summary>Locates Unity Hub installations using conventions for each desktop platform.</summary>
public sealed class UnityHubLocator
{
    private const string WindowsHubExecutable = "Unity Hub.exe";
    private const string WindowsAliasExecutable = "unityhub.exe";
    private const string UnityHubPackageName = "UnityTechnologies.UnityHub";
    private const string PackageRepositoryKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private const string PackagedApplicationsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications";

    /// <summary>Returns the first valid Unity Hub installation found on the current platform.</summary>
    public Task<HubInstallation?> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FindInstallation());
    }

    /// <summary>Validates and normalizes a manually selected executable or macOS app bundle.</summary>
    public string ValidatePath(string value)
    {
        var resolved = ResolveSelectedPath(value.Trim().Trim('"'));
        if (resolved is null || !File.Exists(resolved))
        {
            throw new ArgumentException("Unity Hub 路径不存在或不是文件。", nameof(value));
        }

        if (!IsSupportedExecutableName(Path.GetFileName(resolved)))
        {
            throw new ArgumentException("请选择当前平台的 Unity Hub 可执行程序。", nameof(value));
        }

        return resolved;
    }

    /// <summary>Converts a macOS .app selection to its executable and otherwise returns the original path.</summary>
    internal static string? ResolveSelectedPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.Trim().Trim('"');
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(path, "Contents", "MacOS", "Unity Hub");
        }

        return path;
    }

    /// <summary>Accepts the executable names shipped by Hub 3.x and by the Hub 4.x MSIX package alias.</summary>
    internal static bool IsSupportedExecutableName(string fileName)
    {
        return OperatingSystem.IsWindows()
            ? fileName.Equals(WindowsHubExecutable, StringComparison.OrdinalIgnoreCase)
              || fileName.Equals(WindowsAliasExecutable, StringComparison.OrdinalIgnoreCase)
            : OperatingSystem.IsMacOS()
                ? fileName.Equals("Unity Hub", StringComparison.OrdinalIgnoreCase)
                : fileName.Equals("unityhub", StringComparison.OrdinalIgnoreCase)
                  || fileName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds platform-specific standard install candidates, including PATH entries on Linux.</summary>
    internal static IReadOnlyList<string> GetKnownPaths()
    {
        var paths = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Unity Hub", WindowsHubExecutable));
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Unity Hub", WindowsHubExecutable));
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Unity Hub", WindowsHubExecutable));
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add("/Applications/Unity Hub.app");
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Applications", "Unity Hub.app"));
        }
        else if (OperatingSystem.IsLinux())
        {
            paths.AddRange(["/opt/unityhub/unityhub", "/usr/bin/unityhub", "/usr/local/bin/unityhub"]);
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            paths.AddRange(pathVariable
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(directory => Path.Combine(directory, "unityhub")));
        }

        return paths;
    }

    /// <summary>Resolves the installed Hub, preferring the MSIX package that Hub 4.x replaced 3.x installs with.</summary>
    private static HubInstallation? FindInstallation()
    {
        if (OperatingSystem.IsWindows())
        {
            var packaged = FindWindowsPackagedInstallation();
            if (packaged is not null)
            {
                return packaged;
            }
        }

        var known = GetKnownPaths()
            .Select(ResolveSelectedPath)
            .FirstOrDefault(path => path is not null && File.Exists(path));
        if (known is not null)
        {
            return new HubInstallation(known, "常用安装路径", TryGetFileVersion(known));
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return FindWindowsRegistryInstallation() ?? FindWindowsAppExecutionAlias();
    }

    /// <summary>Finds the MSIX-packaged Hub 4.x build, whose executable lives in the package app directory.</summary>
    [SupportedOSPlatform("windows")]
    private static HubInstallation? FindWindowsPackagedInstallation()
    {
        foreach (var packageRoot in GetWindowsPackageRoots())
        {
            var manifestExecutable = ReadManifestExecutable(Path.Combine(packageRoot, "AppxManifest.xml"));
            foreach (var candidate in GetPackagedCandidates(packageRoot, manifestExecutable))
            {
                if (File.Exists(candidate))
                {
                    return new HubInstallation(candidate, "MSIX 应用包", TryGetFileVersion(candidate));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Falls back to the app execution alias. The alias is a reparse point whose target .NET cannot
    /// resolve, so it is only used after every verifiable location has failed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static HubInstallation? FindWindowsAppExecutionAlias()
    {
        var alias = GetWindowsAppExecutionAlias();
        return alias is not null && File.Exists(alias)
            ? new HubInstallation(alias, "MSIX 应用别名", null)
            : null;
    }

    /// <summary>Reads package roots from the per-user and machine-wide MSIX registration stores.</summary>
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GetWindowsPackageRoots()
    {
        var roots = new List<string>();
        var locations = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64)
        };

        foreach (var (hive, view) in locations)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var repository = baseKey.OpenSubKey(PackageRepositoryKey);
                if (repository is not null)
                {
                    foreach (var packageName in repository.GetSubKeyNames())
                    {
                        using var package = repository.OpenSubKey(packageName);
                        if (package is null
                            || !IsUnityHubPackage(packageName, package.GetValue("DisplayName") as string))
                        {
                            continue;
                        }

                        if (package.GetValue("PackageRootFolder") is string folder && folder.Length > 0)
                        {
                            AddDistinct(roots, folder);
                        }
                    }
                }

                using var applications = baseKey.OpenSubKey(PackagedApplicationsKey);
                if (applications is null)
                {
                    continue;
                }

                foreach (var packageName in applications.GetSubKeyNames())
                {
                    using var package = applications.OpenSubKey(packageName);
                    if (package is null || !IsUnityHubPackage(packageName, null))
                    {
                        continue;
                    }

                    // This store points at the manifest instead of the package root.
                    if (package.GetValue("Path") is string manifestPath && manifestPath.Length > 0)
                    {
                        var folder = Path.GetDirectoryName(manifestPath);
                        if (!string.IsNullOrEmpty(folder))
                        {
                            AddDistinct(roots, folder);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
            {
                // A locked or policy-restricted hive must not prevent fallback discovery.
            }
        }

        return roots;
    }

    /// <summary>Builds executable candidates for an MSIX package root, using the manifest entry point first.</summary>
    internal static IReadOnlyList<string> GetPackagedCandidates(string packageRoot, string? manifestExecutable)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifestExecutable))
        {
            candidates.Add(Path.Combine(
                packageRoot,
                manifestExecutable.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        }

        candidates.Add(Path.Combine(packageRoot, "app", WindowsHubExecutable));
        candidates.Add(Path.Combine(packageRoot, WindowsHubExecutable));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads the packaged application entry point from an AppxManifest.xml file.</summary>
    internal static string? ReadManifestExecutable(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var document = XDocument.Load(manifestPath);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "Application")
                .Select(element => (string?)element.Attribute("Executable"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            // Unreadable or malformed manifests fall back to conventional package layout probing.
            return null;
        }
    }

    /// <summary>Detects Unity Hub package registrations by package family name or display name.</summary>
    internal static bool IsUnityHubPackage(string? packageName, string? displayName)
    {
        if (packageName?.StartsWith(UnityHubPackageName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return displayName?.Trim().Equals("Unity Hub", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>Returns the MSIX app execution alias that the Hub 4.x package registers for its launcher.</summary>
    [SupportedOSPlatform("windows")]
    private static string? GetWindowsAppExecutionAlias()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(localAppData)
            ? null
            : Path.Combine(localAppData, "Microsoft", "WindowsApps", WindowsAliasExecutable);
    }

    /// <summary>Reads the executable file version, tolerating files without version resources.</summary>
    internal static string? TryGetFileVersion(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Appends a registry-derived path while ignoring duplicate entries.</summary>
    private static void AddDistinct(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    /// <summary>Searches Windows uninstall records across machine and user registry views.</summary>
    [SupportedOSPlatform("windows")]
    private static HubInstallation? FindWindowsRegistryInstallation()
    {
        var roots = new[]
        {
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32),
            RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
        };

        try
        {
            foreach (var root in roots)
            {
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(keyName);
                    if (entry is null)
                    {
                        continue;
                    }

                    var displayName = entry.GetValue("DisplayName") as string;
                    if (displayName?.Contains("Unity Hub", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }

                    var version = entry.GetValue("DisplayVersion") as string;
                    foreach (var candidate in GetRegistryCandidates(
                                 entry.GetValue("InstallLocation") as string,
                                 entry.GetValue("DisplayIcon") as string))
                    {
                        if (File.Exists(candidate))
                        {
                            return new HubInstallation(candidate, "Windows 安装记录", version);
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (var root in roots)
            {
                root.Dispose();
            }
        }

        return null;
    }

    /// <summary>Converts registry install and icon values into executable path candidates.</summary>
    internal static IReadOnlyList<string> GetRegistryCandidates(string? installLocation, string? displayIcon)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            paths.Add(Path.Combine(installLocation.Trim().Trim('"'), WindowsHubExecutable));
        }

        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            var path = displayIcon.Split(',')[0].Trim().Trim('"');
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
