namespace HubRoute.Models;

/// <summary>Describes one HTTP proxy candidate and its local reachability state.</summary>
public sealed record ProxyEndpoint(Uri Uri, string Source, bool IsAvailable)
{
    /// <summary>Returns a display-safe URL with any embedded credentials removed.</summary>
    public string DisplayUrl => UriSanitizer.Redact(Uri);
}

/// <summary>Centralizes credential-safe proxy formatting for UI and diagnostics.</summary>
internal static class UriSanitizer
{
    /// <summary>Removes user information while retaining the scheme, host, and port.</summary>
    internal static string Redact(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
