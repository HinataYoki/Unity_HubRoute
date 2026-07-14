namespace HubRoute.Models;

/// <summary>Combines proxy and Unity Hub discovery into one immutable UI snapshot.</summary>
public sealed record EnvironmentSnapshot(
    ProxyEndpoint? Proxy,
    ProxyEndpoint? SystemProxy,
    HubInstallation? Hub,
    string Platform);
