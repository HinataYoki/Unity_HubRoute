namespace HubRoute.Models;

/// <summary>Describes a discovered Unity Hub executable.</summary>
public sealed record HubInstallation(string Path, string Source, string? Version);
