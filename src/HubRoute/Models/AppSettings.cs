namespace HubRoute.Models;

/// <summary>Persisted user preferences that survive application restarts.</summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the saved theme variant name, or null while following the operating system.</summary>
    public string? ThemeVariant { get; set; }
}
