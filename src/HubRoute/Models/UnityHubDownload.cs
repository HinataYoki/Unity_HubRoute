namespace HubRoute.Models;

/// <summary>Describes the official Unity Hub installer for the current platform.</summary>
public sealed record UnityHubDownloadInfo(Uri DownloadUri, string FileName, string PlatformLabel);

/// <summary>Reports byte progress while an installer is being downloaded.</summary>
public sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Returns a percentage when the server provides a content length.</summary>
    public double? Percentage => TotalBytes is > 0
        ? BytesReceived * 100d / TotalBytes.Value
        : null;
}

/// <summary>Reports the completed installer path and number of bytes written.</summary>
public sealed record UnityHubDownloadResult(string FilePath, long BytesReceived);
