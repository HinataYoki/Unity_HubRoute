using System.ComponentModel;
using System.Diagnostics;
using HubRoute.Models;

namespace HubRoute.Services;

/// <summary>Starts Unity Hub with proxy variables scoped only to its child process tree.</summary>
public sealed class HubLauncher(UnityHubLocator locator)
{
    private static readonly TimeSpan HubShutdownTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Stops existing Hub instances, starts a proxy-scoped replacement, and returns its process identifier.</summary>
    public async Task<LaunchResult> LaunchAsync(
        string hubPath,
        Uri? proxyUri,
        CancellationToken cancellationToken = default)
    {
        var validatedPath = locator.ValidatePath(hubPath);
        var stoppedProcessCount = await StopRunningInstancesAsync(validatedPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = BuildStartInfo(validatedPath, proxyUri);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法创建 Unity Hub 进程。");
        return new LaunchResult(
            process.Id,
            proxyUri is null ? "直连模式" : $"代理 {UriSanitizer.Redact(proxyUri)}",
            stoppedProcessCount);
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

    /// <summary>Counts running Unity Editor processes without changing or terminating them.</summary>
    public static int CountRunningUnityEditors()
    {
        var count = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (IsUnityEditorProcessName(process.ProcessName))
                {
                    count++;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                // The process exited or became inaccessible while taking the snapshot.
            }
            finally
            {
                process.Dispose();
            }
        }

        return count;
    }

    /// <summary>Terminates only Unity Hub and its Electron helpers, then waits until no matching process remains.</summary>
    private static async Task<int> StopRunningInstancesAsync(
        string hubPath,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HubShutdownTimeout);
        var stoppedProcessIds = new HashSet<int>();

        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var processes = FindRunningInstances(hubPath);
                if (processes.Count == 0)
                {
                    return stoppedProcessIds.Count;
                }

                try
                {
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (process.HasExited)
                            {
                                continue;
                            }

                            process.Kill();
                            stoppedProcessIds.Add(process.Id);
                        }
                        catch (InvalidOperationException)
                        {
                            // The process exited between discovery and termination.
                        }
                        catch (Win32Exception exception)
                        {
                            throw new InvalidOperationException(
                                $"无法关闭正在运行的 Unity Hub（PID {process.Id}），请手动退出后重试。",
                                exception);
                        }
                    }

                    foreach (var process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                await process.WaitForExitAsync(timeout.Token);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // The process already exited and no longer has a wait handle.
                        }
                    }
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Unity Hub 未能在 8 秒内完全退出，请手动退出后重试。");
        }
    }

    /// <summary>Returns a disposable snapshot of running processes whose names identify Unity Hub components.</summary>
    private static IReadOnlyList<Process> FindRunningInstances(string hubPath)
    {
        var matches = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (IsUnityHubProcessName(process.ProcessName, hubPath))
                {
                    matches.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                process.Dispose();
            }
        }

        return matches;
    }

    /// <summary>Matches canonical Windows, macOS, Linux, AppImage, and Electron helper process names.</summary>
    internal static bool IsUnityHubProcessName(string processName, string hubPath)
    {
        var selectedName = Path.GetFileNameWithoutExtension(hubPath);
        return processName.Equals(selectedName, StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Unity Hub", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UnityHub", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("unityhub", StringComparison.OrdinalIgnoreCase)
               || processName.StartsWith("Unity Hub Helper", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Matches the cross-platform Unity Editor executable without including its helper processes.</summary>
    internal static bool IsUnityEditorProcessName(string processName)
    {
        return processName.Equals("Unity", StringComparison.OrdinalIgnoreCase);
    }
}
