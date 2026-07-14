using HubRoute.Models;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace HubRoute.Services;

/// <summary>Locates Unity Hub installations using conventions for each desktop platform.</summary>
public sealed class UnityHubLocator
{
    /// <summary>Returns the first valid Unity Hub installation found on the current platform.</summary>
    public Task<HubInstallation?> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var known = GetKnownPaths()
            .Select(ResolveSelectedPath)
            .FirstOrDefault(path => path is not null && File.Exists(path));
        if (known is not null)
        {
            return Task.FromResult<HubInstallation?>(new HubInstallation(known, "常用安装路径", null));
        }

        return Task.FromResult(OperatingSystem.IsWindows() ? FindWindowsRegistryInstallation() : null);
    }

    /// <summary>Validates and normalizes a manually selected executable or macOS app bundle.</summary>
    public string ValidatePath(string value)
    {
        var resolved = ResolveSelectedPath(value.Trim().Trim('"'));
        if (resolved is null || !File.Exists(resolved))
        {
            throw new ArgumentException("Unity Hub 路径不存在或不是文件。", nameof(value));
        }

        var fileName = Path.GetFileName(resolved);
        var valid = OperatingSystem.IsWindows()
            ? fileName.Equals("Unity Hub.exe", StringComparison.OrdinalIgnoreCase)
            : OperatingSystem.IsMacOS()
                ? fileName.Equals("Unity Hub", StringComparison.OrdinalIgnoreCase)
                : fileName.Equals("unityhub", StringComparison.OrdinalIgnoreCase)
                  || fileName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
        if (!valid)
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

    /// <summary>Builds platform-specific standard install candidates, including PATH entries on Linux.</summary>
    internal static IReadOnlyList<string> GetKnownPaths()
    {
        var paths = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Unity Hub", "Unity Hub.exe"));
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Unity Hub", "Unity Hub.exe"));
            paths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Unity Hub", "Unity Hub.exe"));
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
            paths.Add(Path.Combine(installLocation.Trim().Trim('"'), "Unity Hub.exe"));
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
