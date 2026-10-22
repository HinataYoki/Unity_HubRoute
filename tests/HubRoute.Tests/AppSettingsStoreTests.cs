using Avalonia.Styling;
using HubRoute.Models;
using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class AppSettingsStoreTests
{
    /// <summary>Restores the theme choice written by an earlier session.</summary>
    [Fact]
    public void SaveAndLoad_ThemeVariant_RoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var store = new AppSettingsStore(Path.Combine(directory.Path, "nested", "settings.json"));

        Assert.True(store.Save(new AppSettings { ThemeVariant = "Dark" }));
        Assert.Equal("Dark", store.Load().ThemeVariant);
    }

    /// <summary>Falls back to defaults when no settings file exists yet.</summary>
    [Fact]
    public void Load_MissingFile_ReturnsDefaultSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new AppSettingsStore(Path.Combine(directory.Path, "settings.json"));

        Assert.Null(store.Load().ThemeVariant);
    }

    /// <summary>Ignores a corrupt settings file instead of failing the window startup.</summary>
    [Fact]
    public void Load_CorruptJson_ReturnsDefaultSettings()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(new AppSettingsStore(path).Load().ThemeVariant);
    }

    /// <summary>Maps persisted names to theme variants and treats anything unknown as the system default.</summary>
    [Theory]
    [InlineData("Dark", true, false)]
    [InlineData("dark", true, false)]
    [InlineData("Light", false, true)]
    [InlineData("light", false, true)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    [InlineData("Solarized", false, false)]
    public void ParseThemeVariant_StoredValues_ReturnExpectedVariant(
        string? stored,
        bool expectDark,
        bool expectLight)
    {
        var variant = AppSettingsStore.ParseThemeVariant(stored);

        Assert.Equal(expectDark, variant == ThemeVariant.Dark);
        Assert.Equal(expectLight, variant == ThemeVariant.Light);
    }

    /// <summary>Writes the variant names that ParseThemeVariant understands.</summary>
    [Fact]
    public void ToSettingValue_ThemeVariants_ReturnStoredNames()
    {
        Assert.Equal("Dark", AppSettingsStore.ToSettingValue(ThemeVariant.Dark));
        Assert.Equal("Light", AppSettingsStore.ToSettingValue(ThemeVariant.Light));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("hubroute-settings").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // Temporary leftovers are harmless for the test result.
            }
        }
    }
}
