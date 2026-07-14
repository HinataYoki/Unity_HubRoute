using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using HubRoute.Models;
using HubRoute.Services;
using Lucide.Avalonia;

namespace HubRoute;

/// <summary>Coordinates the single-window proxy discovery and Unity Hub launch workflow.</summary>
public partial class MainWindow : Window
{
    private readonly ProxyDiscoveryService _proxyDiscovery = new();
    private readonly UnityHubLocator _hubLocator = new();
    private readonly UnityHubDownloadService _hubDownloadService = new();
    private readonly InstallerVerificationService _installerVerificationService = new();
    private readonly HubLauncher _hubLauncher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _downloadCancellation;
    private EnvironmentSnapshot? _snapshot;
    private string? _downloadedInstallerPath;
    private ProxyMode _proxyMode = ProxyMode.Auto;
    private int _logCount;
    private bool _isBusy;

    /// <summary>Initializes platform services and loads the window resources.</summary>
    public MainWindow()
    {
        _hubLauncher = new HubLauncher(_hubLocator);
        InitializeComponent();
        PlatformLabel.Text = GetPlatformName();
        ConfigureHubDownload();
        UpdateModePanels();
        UpdateLaunchState();
    }

    /// <summary>Releases any in-flight platform queries when the window closes.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Runs the first environment scan after all platform services are attached.</summary>
    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        await RefreshEnvironmentAsync();
    }

    /// <summary>Hides the status rail and applies compact page padding in narrow windows.</summary>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var isCompact = e.NewSize.Width < 860;
        Classes.Set("compact", isCompact);
        StatusRail.IsVisible = !isCompact;
        MainContent.Margin = GetResource<Thickness>(isCompact ? "PagePaddingCompact" : "PagePadding");
    }

    /// <summary>Starts a fresh proxy and Unity Hub discovery pass.</summary>
    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await RefreshEnvironmentAsync();
    }

    /// <summary>Keeps the three proxy mode toggles mutually exclusive.</summary>
    private void OnModeToggleClick(object? sender, RoutedEventArgs e)
    {
        _proxyMode = sender switch
        {
            ToggleButton button when button == ManualModeButton => ProxyMode.Manual,
            ToggleButton button when button == DirectModeButton => ProxyMode.Direct,
            _ => ProxyMode.Auto
        };
        UpdateModePanels();
        UpdateLaunchState();
    }

    /// <summary>Revalidates launch readiness when the manual proxy text changes.</summary>
    private void OnManualProxyTextChanged(object? sender, TextChangedEventArgs e)
    {
        SetConnectionBadge(BadgeState.Idle, "待测试");
        UpdateLaunchState();
    }

    /// <summary>Revalidates launch readiness when the Unity Hub path changes.</summary>
    private void OnHubPathTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateHubPathStatus();
        UpdateLaunchState();
    }

    /// <summary>Tests the currently selected proxy without transmitting an HTTP request.</summary>
    private async void OnTestProxyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var uri = GetActiveProxyUri()
                      ?? throw new InvalidOperationException("当前模式没有可测试的代理地址。");
            SetBusy(true);
            var available = await ProxyDiscoveryService.IsAvailableAsync(uri, _lifetimeCancellation.Token);
            SetConnectionBadge(
                available ? BadgeState.Success : BadgeState.Warning,
                available ? "连接正常" : "无响应");
            AddLog(
                $"代理测试：{UriSanitizer.Redact(uri)} {(available ? "可连接" : "无响应")}",
                available ? LogTone.Success : LogTone.Warning);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetConnectionBadge(BadgeState.Error, "地址无效");
            AddLog($"代理测试失败：{GetErrorMessage(exception)}", LogTone.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Opens a native picker suitable for the current platform's Unity Hub package.</summary>
    private async void OnBrowseHubClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? selectedPath;
            if (OperatingSystem.IsMacOS())
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "选择 Unity Hub.app",
                    AllowMultiple = false
                });
                selectedPath = folders.Count > 0 ? folders[0].Path.LocalPath : null;
            }
            else
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择 Unity Hub",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Unity Hub")
                        {
                            Patterns = OperatingSystem.IsWindows()
                                ? ["Unity Hub.exe"]
                                : ["unityhub", "*.AppImage"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
                selectedPath = files.Count > 0 ? files[0].Path.LocalPath : null;
            }

            if (selectedPath is null)
            {
                return;
            }

            HubPathTextBox.Text = UnityHubLocator.ResolveSelectedPath(selectedPath);
            AddLog($"已选择 Unity Hub：{HubPathTextBox.Text}");
        }
        catch (Exception exception)
        {
            AddLog($"无法选择 Unity Hub：{GetErrorMessage(exception)}", LogTone.Error);
        }
    }

    /// <summary>Downloads or cancels the official Unity Hub installer using the selected route.</summary>
    private async void OnDownloadHubClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadCancellation is not null)
        {
            _downloadCancellation.Cancel();
            return;
        }

        var download = UnityHubDownloadService.GetCurrentPlatform();
        if (download is null)
        {
            await OpenHubInstallationDocsAsync();
            return;
        }

        try
        {
            var proxy = GetActiveProxyUri();
            if (_proxyMode != ProxyMode.Direct && proxy is null)
            {
                throw new InvalidOperationException("当前模式尚未配置可用代理。");
            }

            _downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token);
            SetDownloadState(true);
            HubDownloadStatus.Text = $"正在连接 Unity 官方 CDN · {GetRouteDescription(proxy)}";
            var destinationDirectory = await GetDownloadDirectoryAsync();
            var progress = new Progress<DownloadProgress>(UpdateDownloadProgress);
            var result = await _hubDownloadService.DownloadAsync(
                proxy,
                destinationDirectory,
                progress,
                _downloadCancellation.Token);

            HubDownloadStatus.Text = "正在验证 Unity 官方数字签名";
            var verification = await _installerVerificationService.VerifyAsync(
                result.FilePath,
                _downloadCancellation.Token);
            if (!verification.IsTrusted)
            {
                File.Delete(result.FilePath);
                throw new InvalidDataException($"安装包安全校验失败：{verification.Description}");
            }

            _downloadedInstallerPath = result.FilePath;
            HubDownloadProgress.IsIndeterminate = false;
            HubDownloadProgress.Value = 100;
            HubDownloadStatus.Text = $"已保存到 {result.FilePath}";
            ToolTip.SetTip(HubDownloadStatus, result.FilePath);
            OpenHubInstallerButton.IsVisible = true;
            AddLog(
                $"Unity Hub 安装包下载并验证完成：{verification.Description}",
                LogTone.Success);
        }
        catch (OperationCanceledException) when (_downloadCancellation?.IsCancellationRequested == true)
        {
            HubDownloadStatus.Text = "下载已取消";
            AddLog("Unity Hub 安装包下载已取消。", LogTone.Warning);
        }
        catch (Exception exception)
        {
            HubDownloadStatus.Text = $"下载失败：{GetErrorMessage(exception)}";
            ToolTip.SetTip(HubDownloadStatus, HubDownloadStatus.Text);
            AddLog($"Unity Hub 下载失败：{GetErrorMessage(exception)}", LogTone.Error);
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            SetDownloadState(false);
        }
    }

    /// <summary>Opens the completed installer with the operating system's default handler.</summary>
    private async void OnOpenHubInstallerClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadedInstallerPath is null || !File.Exists(_downloadedInstallerPath))
        {
            HubDownloadStatus.Text = "安装包不存在，请重新下载";
            OpenHubInstallerButton.IsVisible = false;
            return;
        }

        var file = await StorageProvider.TryGetFileFromPathAsync(_downloadedInstallerPath);
        var opened = file is not null && await Launcher.LaunchFileAsync(file);
        if (!opened)
        {
            AddLog("无法打开 Unity Hub 安装包。", LogTone.Warning);
        }
    }

    /// <summary>Launches Unity Hub with the selected process-scoped proxy configuration.</summary>
    private void OnLaunchHubClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, launching: true);
            var proxy = GetActiveProxyUri();
            var result = _hubLauncher.Launch(HubPathTextBox.Text ?? string.Empty, proxy);
            AddLog(
                $"Unity Hub 已启动（PID {result.ProcessId}，{result.RouteDescription}）",
                LogTone.Success);
            if (ExitAfterLaunchCheckBox.IsChecked == true)
            {
                Close();
            }
        }
        catch (Exception exception)
        {
            AddLog($"启动失败：{GetErrorMessage(exception)}", LogTone.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Opens Unity's official proxy command-file documentation.</summary>
    private async void OnOpenDocsClick(object? sender, RoutedEventArgs e)
    {
        var opened = await Launcher.LaunchUriAsync(
            new Uri("https://docs.unity3d.com/cn/current/Manual/ent-proxy-cmd-file.html"));
        if (!opened)
        {
            AddLog("无法打开 Unity 官方代理文档。", LogTone.Warning);
        }
    }

    /// <summary>Opens Unity's official installation instructions for unsupported platforms.</summary>
    private async Task OpenHubInstallationDocsAsync()
    {
        var documentationUri = OperatingSystem.IsLinux()
            ? new Uri("https://docs.unity.com/en-us/hub/install-hub-linux")
            : new Uri("https://docs.unity.com/en-us/hub/install-hub");
        var opened = await Launcher.LaunchUriAsync(
            documentationUri);
        if (!opened)
        {
            AddLog("无法打开 Unity Hub 安装文档。", LogTone.Warning);
        }
    }

    /// <summary>Toggles explicitly between light and dark while initially following the operating system.</summary>
    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    /// <summary>Runs proxy and Unity Hub discovery concurrently and applies one coherent snapshot.</summary>
    private async Task RefreshEnvironmentAsync()
    {
        try
        {
            SetBusy(true);
            var proxyTask = _proxyDiscovery.DetectAsync(_lifetimeCancellation.Token);
            var hubTask = _hubLocator.FindAsync(_lifetimeCancellation.Token);
            await Task.WhenAll(proxyTask, hubTask);
            var proxyResult = await proxyTask;
            _snapshot = new EnvironmentSnapshot(
                proxyResult.Preferred,
                proxyResult.System,
                await hubTask,
                GetPlatformName());

            ApplySnapshot(_snapshot);
            var proxySummary = _snapshot.Proxy is null
                ? "未发现可用代理"
                : $"{_snapshot.Proxy.DisplayUrl} · {(_snapshot.Proxy.IsAvailable ? "端口响应正常" : "端口无响应")}";
            AddLog(
                $"环境检测完成：{proxySummary}",
                _snapshot.Proxy?.IsAvailable == true ? LogTone.Success : LogTone.Warning);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AddLog($"环境检测失败：{GetErrorMessage(exception)}", LogTone.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Updates all environment status controls from an immutable discovery result.</summary>
    private void ApplySnapshot(EnvironmentSnapshot snapshot)
    {
        SystemProxyValue.Text = snapshot.SystemProxy?.DisplayUrl ?? "未启用";
        SystemProxySource.Text = snapshot.SystemProxy?.Source ?? snapshot.Platform;
        ToolTip.SetTip(SystemProxyValue, SystemProxyValue.Text);

        DetectedProxyValue.Text = snapshot.Proxy?.DisplayUrl ?? "未检测到代理";
        DetectedProxySource.Text = snapshot.Proxy?.Source ?? "请启动本地代理后重新检测";
        ToolTip.SetTip(DetectedProxyValue, DetectedProxyValue.Text);
        AutoTestButton.IsEnabled = snapshot.Proxy is not null;

        if (snapshot.Proxy is not null && _logCount == 0)
        {
            ManualProxyTextBox.Text = snapshot.Proxy.Uri.GetLeftPart(UriPartial.Authority);
        }

        HubStatusValue.Text = snapshot.Hub is null ? "需要选择" : "已定位";
        HubStatusSource.Text = snapshot.Hub?.Version is not null
            ? $"版本 {snapshot.Hub.Version}"
            : snapshot.Hub?.Source ?? "未找到安装记录";
        if (snapshot.Hub is not null && string.IsNullOrWhiteSpace(HubPathTextBox.Text))
        {
            HubPathTextBox.Text = snapshot.Hub.Path;
        }

        UpdateHubPathStatus();
        SetConnectionBadge(
            snapshot.Proxy?.IsAvailable == true ? BadgeState.Success : BadgeState.Idle,
            snapshot.Proxy?.IsAvailable == true ? "代理可用" : "待测试");
        UpdateLaunchState();
    }

    /// <summary>Shows only the panel associated with the active proxy mode.</summary>
    private void UpdateModePanels()
    {
        AutoModeButton.IsChecked = _proxyMode == ProxyMode.Auto;
        ManualModeButton.IsChecked = _proxyMode == ProxyMode.Manual;
        DirectModeButton.IsChecked = _proxyMode == ProxyMode.Direct;
        AutoProxyPanel.IsVisible = _proxyMode == ProxyMode.Auto;
        ManualProxyPanel.IsVisible = _proxyMode == ProxyMode.Manual;
        DirectProxyPanel.IsVisible = _proxyMode == ProxyMode.Direct;
        ConnectionBadge.IsVisible = _proxyMode != ProxyMode.Direct;
    }

    /// <summary>Validates the selected path and updates its text-and-icon status.</summary>
    private void UpdateHubPathStatus()
    {
        try
        {
            _hubLocator.ValidatePath(HubPathTextBox.Text ?? string.Empty);
            HubPathBadge.Classes.Set("warningPill", false);
            HubPathBadge.Classes.Set("dangerPill", false);
            HubPathBadgeText.Text = "路径已就绪";
        }
        catch (ArgumentException)
        {
            HubPathBadge.Classes.Set("warningPill", true);
            HubPathBadge.Classes.Set("dangerPill", false);
            HubPathBadgeText.Text = string.IsNullOrWhiteSpace(HubPathTextBox.Text) ? "需要选择" : "路径无效";
        }
    }

    /// <summary>Updates the connection badge using both color and explicit text.</summary>
    private void SetConnectionBadge(BadgeState state, string text)
    {
        ConnectionBadge.Classes.Set("warningPill", state == BadgeState.Warning);
        ConnectionBadge.Classes.Set("dangerPill", state == BadgeState.Error);
        ConnectionBadgeText.Text = text;
        ConnectionBadgeIcon.Kind = state switch
        {
            BadgeState.Success => LucideIconKind.Check,
            BadgeState.Warning => LucideIconKind.CircleAlert,
            BadgeState.Error => LucideIconKind.CircleAlert,
            _ => LucideIconKind.SquareActivity
        };
        ConnectionBadgeIcon.Foreground = GetResource<IBrush>(state switch
        {
            BadgeState.Warning => "WarningBrush",
            BadgeState.Error => "DangerBrush",
            _ => "AccentBrush"
        });
    }

    /// <summary>Resolves the selected mode into a validated proxy URI or direct connection.</summary>
    private Uri? GetActiveProxyUri()
    {
        return _proxyMode switch
        {
            ProxyMode.Direct => null,
            ProxyMode.Auto => _snapshot?.Proxy?.Uri,
            ProxyMode.Manual => ProxyDiscoveryService.ParseProxyUri(ManualProxyTextBox.Text ?? string.Empty),
            _ => null
        };
    }

    /// <summary>Enables launch only when the path and selected route contain required values.</summary>
    private void UpdateLaunchState()
    {
        var hasPath = !string.IsNullOrWhiteSpace(HubPathTextBox.Text);
        var hasRoute = _proxyMode == ProxyMode.Direct
                       || (_proxyMode == ProxyMode.Auto && _snapshot?.Proxy is not null)
                       || (_proxyMode == ProxyMode.Manual && !string.IsNullOrWhiteSpace(ManualProxyTextBox.Text));
        LaunchButton.IsEnabled = !_isBusy && hasPath && hasRoute;

        LaunchSummaryText.Text = _proxyMode switch
        {
            ProxyMode.Direct => "直连模式",
            ProxyMode.Auto => _snapshot?.Proxy?.DisplayUrl ?? "等待代理配置",
            ProxyMode.Manual => RedactManualProxy(ManualProxyTextBox.Text),
            _ => "等待代理配置"
        };
        ToolTip.SetTip(LaunchSummaryText, LaunchSummaryText.Text);
    }

    /// <summary>Configures platform-specific download copy and Linux fallback behavior.</summary>
    private void ConfigureHubDownload()
    {
        var download = UnityHubDownloadService.GetCurrentPlatform();
        if (download is null)
        {
            HubDownloadDescription.Text = OperatingSystem.IsLinux()
                ? "Linux 版通过 Unity 官方软件源安装"
                : "当前系统架构没有可用的一键安装包";
            DownloadHubButtonText.Text = "安装说明";
            DownloadHubIcon.Kind = LucideIconKind.ExternalLink;
            HubDownloadStatus.Text = "打开 Unity 官方安装文档";
            return;
        }

        HubDownloadDescription.Text =
            $"从 Unity 官方 CDN 获取 {download.PlatformLabel} 安装包";
        ToolTip.SetTip(HubDownloadDescription, download.DownloadUri.ToString());
    }

    /// <summary>Resolves the operating system's redirected download directory with a safe fallback.</summary>
    private async Task<string> GetDownloadDirectoryAsync()
    {
        var downloads = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
        if (downloads?.Path.IsFile == true
            && !string.IsNullOrWhiteSpace(downloads.Path.LocalPath))
        {
            return downloads.Path.LocalPath;
        }

        var documents = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
        return documents?.Path.IsFile == true
               && !string.IsNullOrWhiteSpace(documents.Path.LocalPath)
            ? documents.Path.LocalPath
            : System.IO.Path.GetTempPath();
    }

    /// <summary>Updates progress controls without blocking the UI thread.</summary>
    private void UpdateDownloadProgress(DownloadProgress progress)
    {
        if (progress.Percentage is double percentage)
        {
            HubDownloadProgress.IsIndeterminate = false;
            HubDownloadProgress.Value = percentage;
            HubDownloadStatus.Text =
                $"{FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes!.Value)} · {percentage:F0}%";
        }
        else
        {
            HubDownloadProgress.IsIndeterminate = true;
            HubDownloadStatus.Text = $"已下载 {FormatBytes(progress.BytesReceived)}";
        }
    }

    /// <summary>Switches the download command between start and cancellation states.</summary>
    private void SetDownloadState(bool isDownloading)
    {
        HubDownloadProgress.IsVisible = isDownloading || _downloadedInstallerPath is not null;
        DownloadHubButtonText.Text = isDownloading
            ? "取消下载"
            : UnityHubDownloadService.GetCurrentPlatform() is null
                ? "安装说明"
                : "一键下载";
        DownloadHubIcon.Kind = isDownloading
            ? LucideIconKind.CircleX
            : UnityHubDownloadService.GetCurrentPlatform() is null
                ? LucideIconKind.ExternalLink
                : LucideIconKind.Download;
        OpenHubInstallerButton.IsVisible =
            !isDownloading
            && _downloadedInstallerPath is not null
            && File.Exists(_downloadedInstallerPath);
    }

    /// <summary>Returns a credential-safe description of the route used for downloading.</summary>
    private static string GetRouteDescription(Uri? proxy)
    {
        return proxy is null
            ? "直连"
            : $"代理 {UriSanitizer.Redact(proxy)}";
    }

    /// <summary>Formats byte counts into compact binary units for progress display.</summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    /// <summary>Disables conflicting commands during native platform work.</summary>
    private void SetBusy(bool isBusy, bool launching = false)
    {
        _isBusy = isBusy;
        AutoTestButton.IsEnabled = !isBusy && _snapshot?.Proxy is not null;
        RefreshIcon.Opacity = isBusy ? 0.45 : 1;
        LaunchButtonText.Text = launching ? "正在启动" : "启动 Unity Hub";
        UpdateLaunchState();
    }

    /// <summary>Adds one bounded, screen-reader-visible activity row.</summary>
    private void AddLog(string message, LogTone tone = LogTone.Neutral)
    {
        if (EmptyLogRow.Parent is Panel parent)
        {
            parent.Children.Remove(EmptyLogRow);
        }

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("54,8,*"),
            MinHeight = 31,
            ColumnSpacing = 8
        };
        var time = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm:ss"),
            Classes = { "muted", "mono" },
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var dot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Classes = { tone switch
            {
                LogTone.Success => "logSuccess",
                LogTone.Warning => "logWarning",
                LogTone.Error => "logError",
                _ => "logNeutral"
            } },
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = message,
            FontSize = GetResource<double>("FontSizeXs"),
            Foreground = GetResource<IBrush>(tone == LogTone.Error ? "DangerBrush" : "TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(dot, 1);
        Grid.SetColumn(text, 2);
        row.Children.Add(time);
        row.Children.Add(dot);
        row.Children.Add(text);

        var container = new Border
        {
            BorderBrush = GetResource<IBrush>("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
        ActivityLogPanel.Children.Insert(0, container);
        while (ActivityLogPanel.Children.Count > 8)
        {
            ActivityLogPanel.Children.RemoveAt(ActivityLogPanel.Children.Count - 1);
        }

        _logCount++;
        LogCountText.Text = $"{Math.Min(_logCount, 8)} 条";
    }

    /// <summary>Redacts credentials from a manual proxy without throwing during text entry.</summary>
    private static string RedactManualProxy(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value)
                ? "等待代理配置"
                : UriSanitizer.Redact(ProxyDiscoveryService.ParseProxyUri(value));
        }
        catch (ArgumentException)
        {
            return value ?? "等待代理配置";
        }
    }

    /// <summary>Resolves a typed design token for the window's active theme.</summary>
    private T GetResource<T>(string key)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var value) == true
            && value is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"缺少界面资源：{key}");
    }

    /// <summary>Returns the current desktop platform name for status and diagnostics.</summary>
    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return OperatingSystem.IsLinux() ? "Linux" : "Desktop";
    }

    /// <summary>Produces a concise message for exceptions crossing the platform boundary.</summary>
    private static string GetErrorMessage(Exception exception)
    {
        return exception.Message.Trim().TrimEnd('.');
    }

    private enum ProxyMode
    {
        Auto,
        Manual,
        Direct
    }

    private enum BadgeState
    {
        Idle,
        Success,
        Warning,
        Error
    }

    private enum LogTone
    {
        Neutral,
        Success,
        Warning,
        Error
    }
}
