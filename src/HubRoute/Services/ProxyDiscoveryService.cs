using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using HubRoute.Models;
using Microsoft.Win32;

namespace HubRoute.Services;

/// <summary>Discovers explicit HTTP proxies from the active desktop platform.</summary>
public sealed partial class ProxyDiscoveryService
{
    private static readonly int[] CommonLocalPorts = [7890, 7891, 1080, 8080, 8888, 10809, 10810];
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>Finds the preferred reachable proxy while retaining the system candidate for diagnostics.</summary>
    public async Task<(ProxyEndpoint? Preferred, ProxyEndpoint? System)> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        var system = await ReadSystemProxyAsync(cancellationToken);
        if (system is not null)
        {
            system = system with { IsAvailable = await IsAvailableAsync(system.Uri, cancellationToken) };
            if (system.IsAvailable)
            {
                return (system, system);
            }
        }

        foreach (var port in CommonLocalPorts)
        {
            var uri = new Uri($"http://127.0.0.1:{port}");
            if (await IsAvailableAsync(uri, cancellationToken))
            {
                return (new ProxyEndpoint(uri, "常用本地端口", true), system);
            }
        }

        return (system, system);
    }

    /// <summary>Normalizes a user-entered proxy and validates its supported protocol.</summary>
    public static Uri ParseProxyUri(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("代理地址不能为空。", nameof(value));
        }

        var candidate = trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : $"http://{trimmed}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.Port <= 0)
        {
            throw new ArgumentException("请输入包含主机和端口的 HTTP 或 HTTPS 代理地址。", nameof(value));
        }

        return uri;
    }

    /// <summary>Checks TCP reachability without sending an HTTP request through the proxy.</summary>
    public static async Task<bool> IsAvailableAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, uri.Port, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Reads the platform's explicit proxy configuration with environment variables as fallback.</summary>
    private static async Task<ProxyEndpoint?> ReadSystemProxyAsync(CancellationToken cancellationToken)
    {
        ProxyEndpoint? endpoint = null;
        if (OperatingSystem.IsWindows())
        {
            endpoint = ReadWindowsProxy();
        }
        else if (OperatingSystem.IsMacOS())
        {
            endpoint = ParseMacOsProxy(await RunCommandAsync("/usr/sbin/scutil", "--proxy", cancellationToken));
        }
        else if (OperatingSystem.IsLinux())
        {
            endpoint = await ReadLinuxProxyAsync(cancellationToken);
        }

        return endpoint ?? ReadEnvironmentProxy();
    }

    /// <summary>Reads Windows Internet Settings for the current user.</summary>
    [SupportedOSPlatform("windows")]
    private static ProxyEndpoint? ReadWindowsProxy()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        if (settings?.GetValue("ProxyEnable") is not int enabled || enabled != 1)
        {
            return null;
        }

        return settings.GetValue("ProxyServer") is string server
            ? ParseWindowsProxyServer(server)
            : null;
    }

    /// <summary>Extracts the HTTP endpoint from a Windows single or per-protocol proxy string.</summary>
    internal static ProxyEndpoint? ParseWindowsProxyServer(string value)
    {
        var selected = value.Trim();
        if (selected.Contains('='))
        {
            selected = selected
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .FirstOrDefault(parts => parts[0].Equals("http", StringComparison.OrdinalIgnoreCase))?[1]
                ?? string.Empty;
        }

        if (selected.Length == 0)
        {
            return null;
        }

        try
        {
            return new ProxyEndpoint(ParseProxyUri(selected), "Windows 系统代理", false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Parses the HTTP proxy fields emitted by macOS scutil.</summary>
    internal static ProxyEndpoint? ParseMacOsProxy(string output)
    {
        if (!TryReadScutilValue(output, "HTTPEnable", out var enabled) || enabled != "1"
            || !TryReadScutilValue(output, "HTTPProxy", out var host)
            || !TryReadScutilValue(output, "HTTPPort", out var port))
        {
            return null;
        }

        try
        {
            return new ProxyEndpoint(ParseProxyUri($"http://{host}:{port}"), "macOS 系统代理", false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads one named value from scutil's dictionary-style output.</summary>
    private static bool TryReadScutilValue(string output, string key, out string value)
    {
        var match = ScutilLineRegex().Match(output);
        while (match.Success)
        {
            if (match.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = match.Groups["value"].Value.Trim();
                return true;
            }

            match = match.NextMatch();
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Reads GNOME's explicit HTTP proxy when Linux environment variables are absent.</summary>
    private static async Task<ProxyEndpoint?> ReadLinuxProxyAsync(CancellationToken cancellationToken)
    {
        var environment = ReadEnvironmentProxy();
        if (environment is not null)
        {
            return environment;
        }

        var mode = (await RunCommandAsync("gsettings", "get org.gnome.system.proxy mode", cancellationToken))
            .Trim().Trim('\'');
        if (!mode.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var host = (await RunCommandAsync("gsettings", "get org.gnome.system.proxy.http host", cancellationToken))
            .Trim().Trim('\'');
        var port = (await RunCommandAsync("gsettings", "get org.gnome.system.proxy.http port", cancellationToken))
            .Trim();
        if (host.Length == 0 || port.Length == 0)
        {
            return null;
        }

        try
        {
            return new ProxyEndpoint(ParseProxyUri($"http://{host}:{port}"), "Linux 桌面代理", false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads standard HTTP proxy variables without persisting them.</summary>
    private static ProxyEndpoint? ReadEnvironmentProxy()
    {
        var value = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                    ?? Environment.GetEnvironmentVariable("https_proxy")
                    ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                    ?? Environment.GetEnvironmentVariable("http_proxy");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return new ProxyEndpoint(ParseProxyUri(value), "环境变量", false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Runs a short platform query and returns empty output when the utility is unavailable.</summary>
    private static async Task<string> RunCommandAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return string.Empty;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"(?m)^\s*(?<key>[A-Za-z]+)\s*:\s*(?<value>.+?)\s*$")]
    private static partial Regex ScutilLineRegex();
}
