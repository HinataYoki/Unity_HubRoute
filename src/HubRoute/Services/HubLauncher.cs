using System.Diagnostics;
using HubRoute.Models;

namespace HubRoute.Services;

/// <summary>Starts Unity Hub with proxy variables scoped only to its child process tree.</summary>
public sealed class HubLauncher(UnityHubLocator locator)
{
    /// <summary>Validates inputs, creates the child process, and returns its process identifier.</summary>
    public LaunchResult Launch(string hubPath, Uri? proxyUri)
    {
        var validatedPath = locator.ValidatePath(hubPath);
        var startInfo = BuildStartInfo(validatedPath, proxyUri);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法创建 Unity Hub 进程。");
        return new LaunchResult(
            process.Id,
            proxyUri is null ? "直连模式" : $"代理 {UriSanitizer.Redact(proxyUri)}");
    }

    /// <summary>Builds the cross-platform process start configuration without changing global state.</summary>
    internal static ProcessStartInfo BuildStartInfo(string hubPath, Uri? proxyUri)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = hubPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(hubPath) ?? Environment.CurrentDirectory
        };

        if (proxyUri is null)
        {
            foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
            {
                startInfo.Environment.Remove(key);
            }
        }
        else
        {
            var value = proxyUri.GetLeftPart(UriPartial.Authority);
            startInfo.Environment["HTTP_PROXY"] = value;
            startInfo.Environment["HTTPS_PROXY"] = value;
            startInfo.Environment["http_proxy"] = value;
            startInfo.Environment["https_proxy"] = value;
        }

        return startInfo;
    }
}
