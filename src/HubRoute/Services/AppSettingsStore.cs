using System.Text.Json;
using Avalonia.Styling;
using HubRoute.Models;

namespace HubRoute.Services;

/// <summary>
/// Stores user preferences in the per-user configuration directory. The theme toggle only changes
/// the running application, so the selected variant has to be written to disk to survive a restart.
/// </summary>
public sealed class AppSettingsStore
{
    private const string DirectoryName = "HubRoute";
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    /// <summary>Initializes a store for the default per-user settings file.</summary>
    public AppSettingsStore()
        : this(GetDefaultSettingsPath())
    {
    }

    /// <summary>Initializes a store for an explicit path, used by tests and future portable builds.</summary>
    internal AppSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>Returns the settings file inside the per-user application data directory.</summary>
    internal static string GetDefaultSettingsPath()
    {
        // ApplicationData maps to %APPDATA% on Windows and to $XDG_CONFIG_HOME on Linux.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(root, DirectoryName, FileName);
    }

    /// <summary>Loads the stored settings, falling back to defaults when the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or NotSupportedException)
        {
            // A corrupt or unreadable settings file must never prevent the window from opening.
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings and reports whether the file could be updated.</summary>
    public bool Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Converts a stored value into a theme variant, returning null for the system default.</summary>
    internal static ThemeVariant? ParseThemeVariant(string? value)
    {
        return value?.Trim() switch
        {
            var name when string.Equals(name, nameof(ThemeVariant.Dark), StringComparison.OrdinalIgnoreCase)
                => ThemeVariant.Dark,
            var name when string.Equals(name, nameof(ThemeVariant.Light), StringComparison.OrdinalIgnoreCase)
                => ThemeVariant.Light,
            _ => null
        };
    }

    /// <summary>Converts the active theme variant into the value written to disk.</summary>
    internal static string ToSettingValue(ThemeVariant variant)
    {
        return variant == ThemeVariant.Dark ? nameof(ThemeVariant.Dark) : nameof(ThemeVariant.Light);
    }
}
