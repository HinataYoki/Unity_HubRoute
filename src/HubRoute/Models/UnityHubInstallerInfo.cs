namespace HubRoute.Models;

/// <summary>Describes an official Unity Hub installer link for a supported platform.</summary>
public sealed record UnityHubInstallerInfo(Uri DownloadUri, string FileName, string PlatformLabel);
