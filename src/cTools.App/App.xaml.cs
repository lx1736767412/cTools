using System.Windows;
using System.Windows.Interop;
using cTools.Core.Logging;
using cTools.Core.Storage;
using cTools.App.Services;
using cTools.App.ViewModels;

namespace cTools.App;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private TrayService? _tray;
    private HotkeyService? _hotkey;
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
            ? new TrayService(ShowPalette, OpenSettings, RebuildIndex, ExitApp)
            : null;
        ReloadHotkey(useFallback: false);

        _viewModel.SettingsChanged += OnSettingsChanged;
        _viewModel.Initialize();
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
            _tray ??= new TrayService(ShowPalette, OpenSettings, RebuildIndex, ExitApp);
            _tray.SetVisible(true);
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
        }
        _mainWindow?.ApplySettingsToWindow();
        ReloadHotkey(useFallback: true);
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
