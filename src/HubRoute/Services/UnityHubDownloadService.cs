using System.Net;
using System.Runtime.InteropServices;
using HubRoute.Models;

namespace HubRoute.Services;

/// <summary>Downloads the official Unity Hub installer through an optional process-scoped proxy.</summary>
public sealed class UnityHubDownloadService
{
    private const string InstallerBaseUrl = "https://public-cdn.cloud.unity3d.com/hub/prod/";
    private const int BufferSize = 64 * 1024;

    /// <summary>Returns the official CDN metadata for the current desktop platform.</summary>
    public static UnityHubDownloadInfo? GetCurrentPlatform()
    {
        var platform = OperatingSystem.IsWindows()
            ? OSPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? OSPlatform.OSX
                : OSPlatform.Linux;
        return GetPlatformDownload(platform, RuntimeInformation.OSArchitecture);
    }

    /// <summary>Maps a desktop platform and CPU architecture to an official installer.</summary>
    internal static UnityHubDownloadInfo? GetPlatformDownload(
        OSPlatform platform,
        Architecture architecture)
    {
        var architectureLabel = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (architectureLabel is null)
        {
            return null;
        }

        if (platform == OSPlatform.Windows)
        {
            return CreateDownloadInfo($"UnityHubSetup-{architectureLabel}.exe", "Windows");
        }

        return platform == OSPlatform.OSX
            ? CreateDownloadInfo($"UnityHubSetup-{architectureLabel}.dmg", "macOS")
            : null;
    }

    /// <summary>Downloads the installer atomically and reports progress as bytes are written.</summary>
    public async Task<UnityHubDownloadResult> DownloadAsync(
        Uri? proxyUri,
        string destinationDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var download = GetCurrentPlatform()
                       ?? throw new PlatformNotSupportedException("当前平台暂不支持一键下载 Unity Hub，请查看官方安装文档。");
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("下载目录不能为空。", nameof(destinationDirectory));
        }

        var resolvedDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(resolvedDirectory);
        var destinationPath = Path.Combine(resolvedDirectory, download.FileName);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.download";
        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = proxyUri is not null,
                Proxy = proxyUri is null ? null : CreateProxy(proxyUri)
            };
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            using var response = await client.GetAsync(
                download.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            long bytesReceived = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await using var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = new byte[BufferSize];
                progress?.Report(new DownloadProgress(0, totalBytes));
                while (true)
                {
                    var bytesRead = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    bytesReceived += bytesRead;
                    progress?.Report(new DownloadProgress(bytesReceived, totalBytes));
                }
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return new UnityHubDownloadResult(destinationPath, bytesReceived);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Builds an authenticated proxy without retaining credentials in its address.</summary>
    internal static WebProxy CreateProxy(Uri proxyUri)
    {
        var address = new UriBuilder(proxyUri)
        {
            UserName = string.Empty,
            Password = string.Empty
        }.Uri;
        var proxy = new WebProxy(address);
        if (string.IsNullOrEmpty(proxyUri.UserInfo))
        {
            return proxy;
        }

        var userInfo = proxyUri.UserInfo.Split(':', 2);
        proxy.Credentials = new NetworkCredential(
            Uri.UnescapeDataString(userInfo[0]),
            userInfo.Length == 2 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty);
        return proxy;
    }

    /// <summary>Builds one immutable installer description from a CDN filename and platform label.</summary>
    private static UnityHubDownloadInfo CreateDownloadInfo(string fileName, string platformLabel)
    {
        return new UnityHubDownloadInfo(
            new Uri(InstallerBaseUrl + fileName),
            fileName,
            platformLabel);
    }
}
