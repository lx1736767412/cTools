using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using cTools.Core.Logging;
using cTools.Core.Storage;
using cTools.Core.Updating;
using cTools.App.Services;
using cTools.App.ViewModels;

namespace cTools.App;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private TrayService? _tray;
    private HotkeyService? _hotkey;
    private UpdateService? _updates;
    private UpdateWindow? _updateWindow;
    private DispatcherTimer? _updateTimer;
    private CancellationTokenSource? _updateScheduleCts;
    private bool _isCheckingUpdates;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Init();
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("未处理的 UI 异常", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("未处理的应用异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        if (!SingleInstance.TryAcquire(() => Dispatcher.Invoke(ShowPalette)))
        {
            Shutdown();
            return;
        }

        _viewModel = new MainViewModel();
        _mainWindow = new MainWindow(_viewModel);
        MainWindow = _mainWindow;
        _mainWindow.EnsureHandle();
        ThemeService.Apply(_viewModel.Settings.Theme, _mainWindow);

        _tray = _viewModel.Settings.ShowTrayIcon
            ? new TrayService(ShowPalette, OpenSettings, RebuildIndex, CheckUpdatesFromTray, ExitApp)
            : null;
        ReloadHotkey(useFallback: false);

        _viewModel.SettingsChanged += OnSettingsChanged;
        _viewModel.Initialize();

        _updates = new UpdateService();
        _updates.CleanupOldDownloads();
        RestartUpdateScheduler();
        try
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // 无交互桌面 / 远程会话中可能不可用。
        }

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            _mainWindow.ShowPalette();
            var queryIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--query", StringComparison.OrdinalIgnoreCase));
            if (queryIndex >= 0 && queryIndex + 1 < e.Args.Length)
                _mainWindow.SetQuery(e.Args[queryIndex + 1]);
            if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            {
                var tabIndex = 0;
                var tabArg = Array.FindIndex(e.Args, a => string.Equals(a, "--tab", StringComparison.OrdinalIgnoreCase));
                if (tabArg >= 0 && tabArg + 1 < e.Args.Length)
                    int.TryParse(e.Args[tabArg + 1], out tabIndex);
                _mainWindow.OpenSettings(tabIndex);
            }
        }
    }

    public void ShowPalette()
    {
        if (_mainWindow is null) return;
        _mainWindow.ShowPalette();
    }

    private void ShowPaletteFromHotkey()
    {
        if (_mainWindow is null || _viewModel is null) return;
        if (_viewModel.Settings.SuppressHotkeyInFullscreen && IsFullscreen(_mainWindow))
        {
            Log.Info("全屏状态：忽略快捷键呼出");
            return;
        }
        _mainWindow.ShowPalette();
    }

    private bool IsHotkeySuppressed()
        => _viewModel?.Settings.SuppressHotkeyInFullscreen == true
           && _mainWindow is not null
           && IsFullscreen(_mainWindow);

    private static bool IsFullscreen(Window window)
        => FullscreenGuard.IsSuppressed(new WindowInteropHelper(window).Handle);

    public void OpenSettings()
    {
        _mainWindow?.OpenSettings();
    }

    public void RebuildIndex()
    {
        _viewModel?.RebuildIndexAsync();
    }

    public void ExitApp()
    {
        if (_isExiting) return;
        _isExiting = true;
        _updateScheduleCts?.Cancel();
        _updateTimer?.Stop();
        _tray?.Dispose();
        _tray = null;
        _hotkey?.Dispose();
        _hotkey = null;
        SingleInstance.Dispose();
        _mainWindow?.CloseForExit();
        Shutdown();
    }

    private void OnSettingsChanged()
    {
        if (_viewModel is null) return;
        ThemeService.Apply(_viewModel.Settings.Theme, _mainWindow);
        if (_viewModel.Settings.ShowTrayIcon)
        {
            _tray ??= new TrayService(ShowPalette, OpenSettings, RebuildIndex, CheckUpdatesFromTray, ExitApp);
            _tray.SetVisible(true);
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
        }
        _mainWindow?.ApplySettingsToWindow();
        if (!string.Equals(_hotkey?.Hotkey, _viewModel.Settings.Hotkey, StringComparison.OrdinalIgnoreCase))
            ReloadHotkey(useFallback: true);
        RestartUpdateScheduler();
    }

    private void ReloadHotkey(bool useFallback)
    {
        if (_mainWindow is null || _viewModel is null) return;
        var previous = _hotkey?.Hotkey;
        var attempted = _viewModel.Settings.Hotkey;
        _hotkey?.Dispose();
        _hotkey = null;

        var service = new HotkeyService(_mainWindow, _viewModel.Settings.Hotkey, ShowPaletteFromHotkey, IsHotkeySuppressed);
        if (service.Register())
        {
            _hotkey = service;
            return;
        }

        service.Dispose();
        if (useFallback && !string.IsNullOrWhiteSpace(previous))
        {
            var fallback = new HotkeyService(_mainWindow, previous, ShowPaletteFromHotkey, IsHotkeySuppressed);
            if (fallback.Register())
            {
                _hotkey = fallback;
                _viewModel.Settings.Hotkey = previous;
                ConfigStore.Save(_viewModel.Settings);
                _viewModel.Status = $"快捷键 {attempted} 注册失败，仍使用 {previous}";
                return;
            }
            fallback.Dispose();
        }
        _viewModel.Status = $"全局快捷键 {_viewModel.Settings.Hotkey} 注册失败";
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_viewModel?.Settings.Theme != cTools.Core.Models.ThemeMode.System || _mainWindow is null) return;
        try
        {
            Dispatcher.BeginInvoke(() => ThemeService.Apply(cTools.Core.Models.ThemeMode.System, _mainWindow));
        }
        catch
        {
            // 应用正在退出时忽略。
        }
    }

    // ── 更新 ─────────────────────────────────────────────────

    /// <summary>检查 GitHub Releases；返回是否已为用户打开更新窗口。</summary>
    public async Task<bool> CheckForUpdatesAsync(bool manual)
    {
        if (_updates is null || _isCheckingUpdates || _isExiting) return false;
        if (!manual && (!_updates.IsInstalled || _viewModel?.Settings.AutoCheckUpdates != true)) return false;

        _isCheckingUpdates = true;
        try
        {
            var update = await _updates.CheckAsync();
            if (update is null || update.Version <= UpdateService.CurrentVersion) return false;

            var settings = _viewModel?.Settings;
            if (!manual && settings is not null
                && string.Equals(settings.SkippedUpdateTag, update.Tag, StringComparison.OrdinalIgnoreCase))
                return false;

            ShowUpdateWindow(update);
            return true;
        }
        finally
        {
            _isCheckingUpdates = false;
        }
    }

    private void ShowUpdateWindow(UpdateInfo update)
    {
        if (_updateWindow is { IsLoaded: true })
        {
            _updateWindow.Activate();
            return;
        }
        if (_updates is null || _viewModel is null) return;

        _updateWindow = new UpdateWindow(update, _updates, _viewModel.Settings, _updates.IsInstalled);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
        _updateWindow.Topmost = true;
        _updateWindow.Activate();
        _updateWindow.Topmost = false;
    }

    private async void CheckUpdatesFromTray()
    {
        try
        {
            var found = await CheckForUpdatesAsync(manual: true);
            if (!found)
                MessageBox.Show("当前已是最新版本（" + UpdateService.CurrentVersionText + "）。", "cTools",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("检查更新失败", ex);
            MessageBox.Show("检查更新失败：" + ex.Message, "cTools",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestartUpdateScheduler()
    {
        _updateScheduleCts?.Cancel();
        _updateTimer?.Stop();
        _updateTimer = null;

        if (_viewModel?.Settings.AutoCheckUpdates != true) return;

        var cts = new CancellationTokenSource();
        _updateScheduleCts = cts;
        _ = RunUpdateScheduleAsync(cts);
    }

    private async Task RunUpdateScheduleAsync(CancellationTokenSource cts)
    {
        try
        {
            // 启动后稍等片刻再访问网络，避免和索引扫描、首帧渲染抢资源。
            await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);
            if (cts.IsCancellationRequested || _isExiting) return;
            await CheckForUpdatesInBackgroundAsync();

            if (cts.IsCancellationRequested || _isExiting) return;
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _updateTimer.Tick += async (_, _) => await CheckForUpdatesInBackgroundAsync();
            _updateTimer.Start();
        }
        catch (OperationCanceledException)
        {
            // 设置变更或应用退出，结束本轮计划。
        }
        catch (Exception ex)
        {
            Log.Error("自动检查更新失败", ex);
        }
        finally
        {
            if (ReferenceEquals(_updateScheduleCts, cts)) _updateScheduleCts = null;
            cts.Dispose();
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        if (_updates?.IsInstalled != true || _viewModel?.Settings.AutoCheckUpdates != true) return;
        try
        {
            await CheckForUpdatesAsync(manual: false);
        }
        catch (Exception ex)
        {
            Log.Error("自动检查更新失败", ex);
        }
    }

    /// <summary>启动安装程序后退出当前进程，由安装包负责重新启动。</summary>
    public void ExitForUpdate()
    {
        Log.Info("退出 cTools 以安装更新");
        ExitApp();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
            // 忽略。
        }
        _tray?.Dispose();
        _hotkey?.Dispose();
        SingleInstance.Dispose();
        base.OnExit(e);
    }
}
