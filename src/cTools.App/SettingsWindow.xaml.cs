using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using cTools.App.Services;
using cTools.App.ViewModels;
using cTools.Core.Models;
using cTools.Core.Storage;
using WinForms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using AppTheme = cTools.Core.Models.ThemeMode;

namespace cTools.App;

public partial class SettingsWindow : Window
{
    private static readonly string[] KnownExtensions =
    {
        ".exe", ".lnk", ".url", ".appref-ms", ".bat", ".cmd", ".ps1", ".msi"
    };

    public sealed record MonitorChoice(string Label, MonitorPreference Mode, string? DeviceName)
    {
        public override string ToString() => Label;
    }

    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly List<MonitorChoice> _monitorChoices = new();
    private readonly DispatcherTimer _saveFeedbackTimer;
    private bool _previewed;
    private bool _hasSaved;

    public SettingsWindow(MainViewModel viewModel, int initialTab = 0)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = viewModel.Settings.Clone();
        SettingsTabs.SelectedIndex = Math.Clamp(initialTab, 0, 5);
        _saveFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _saveFeedbackTimer.Tick += (_, _) =>
        {
            _saveFeedbackTimer.Stop();
            SaveStatusText.Visibility = Visibility.Collapsed;
        };
        SourceInitialized += (_, _) => ApplyWindowTheme();
        Loaded += (_, _) => LoadControls();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            _saveFeedbackTimer.Stop();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            // 预览重建只影响当前会话；关闭时恢复已保存设置对应的索引。
            if (_previewed) _ = _viewModel.RebuildIndexAsync();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Cancel_Click(this, new RoutedEventArgs());
            e.Handled = true;
        };
    }

    private void ApplyWindowTheme()
    {
        var dark = _settings.Theme == AppTheme.Dark
            || (_settings.Theme == AppTheme.System && ThemeService.IsSystemDark());
        WindowEffects.Apply(this, dark);
    }

    private void ShowSavedFeedback()
    {
        SaveStatusText.Text = "已保存";
        SaveStatusText.Visibility = Visibility.Visible;
        _saveFeedbackTimer.Stop();
        _saveFeedbackTimer.Start();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ScanSummary))
            ScanSummaryText.Text = _viewModel.ScanSummary;
    }

    private void LoadControls()
    {
        ThemeSystemRadio.IsChecked = _settings.Theme == AppTheme.System;
        ThemeLightRadio.IsChecked = _settings.Theme == AppTheme.Light;
        ThemeDarkRadio.IsChecked = _settings.Theme == AppTheme.Dark;

        LayoutListRadio.IsChecked = _settings.ResultLayout == ResultLayout.List;
        LayoutGridRadio.IsChecked = _settings.ResultLayout == ResultLayout.Grid;

        PosTopRadio.IsChecked = _settings.PalettePosition == PalettePosition.Top;
        PosUpperRadio.IsChecked = _settings.PalettePosition == PalettePosition.UpperCenter;
        PosCenterRadio.IsChecked = _settings.PalettePosition == PalettePosition.Center;
        PosLowerRadio.IsChecked = _settings.PalettePosition == PalettePosition.LowerCenter;
        PosBottomRadio.IsChecked = _settings.PalettePosition == PalettePosition.Bottom;

        EscClearRadio.IsChecked = _settings.EscapeBehavior == EscapeBehavior.ClearThenHide;
        EscHideRadio.IsChecked = _settings.EscapeBehavior == EscapeBehavior.AlwaysHide;

        StartWithWindowsBox.IsChecked = _settings.StartWithWindows || AutoStart.IsEnabled();
        ShowTrayBox.IsChecked = _settings.ShowTrayIcon;
        HideOnBlurBox.IsChecked = _settings.HideOnBlur;
        SelectSearchTextBox.IsChecked = _settings.SelectSearchTextOnOpen;
        KeepSearchTextBox.IsChecked = _settings.KeepSearchText;
        CompactModeBox.IsChecked = _settings.CompactMode;
        SpaceToLaunchBox.IsChecked = _settings.SpaceToLaunch;
        SuppressFullscreenBox.IsChecked = _settings.SuppressHotkeyInFullscreen;
        MaxResultsBox.Text = _settings.MaxResults.ToString();

        StartMenuBox.IsChecked = _settings.Search.StartMenu;
        DesktopBox.IsChecked = _settings.Search.Desktop;
        AppPathsBox.IsChecked = _settings.Search.AppPaths;
        UwpBox.IsChecked = _settings.Search.Uwp;
        SystemSettingsBox.IsChecked = _settings.Search.SystemSettings;
        CustomEntriesBox.IsChecked = _settings.Search.CustomEntries;
        CustomPathsBox.IsChecked = _settings.Search.CustomPaths;
        IncludeSubfoldersBox.IsChecked = _settings.Search.IncludeSubfolders;
        MaxDepthBox.Text = _settings.Search.MaxScanDepth.ToString();

        var extensions = new HashSet<string>(
            _settings.ScanExtensions.Select(EntryKindDetector.NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);
        ScanExeBox.IsChecked = extensions.Contains(".exe");
        ScanLnkBox.IsChecked = extensions.Contains(".lnk");
        ScanUrlBox.IsChecked = extensions.Contains(".url");
        ScanAppRefBox.IsChecked = extensions.Contains(".appref-ms");
        ScanBatBox.IsChecked = extensions.Contains(".bat") || extensions.Contains(".cmd");
        ScanPs1Box.IsChecked = extensions.Contains(".ps1");
        ScanMsiBox.IsChecked = extensions.Contains(".msi");
        var extras = _settings.ScanExtensions
            .Select(EntryKindDetector.NormalizeExtension)
            .Where(extension => extension.Length > 1 && !KnownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        ExtraExtensionsBox.Text = string.Join(", ", extras);

        CustomPathsList.ItemsSource = _settings.CustomPaths;
        CustomEntriesList.ItemsSource = _settings.CustomEntries;
        UpdatePathsCount();
        ExcludePatternsBox.Text = string.Join(Environment.NewLine, _settings.ExcludePatterns);
        HotkeyBox.Text = _settings.Hotkey;
        ScanSummaryText.Text = _viewModel.ScanSummary;
        VersionText.Text = "版本 " + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "1.0.0");
        DataPathText.Text = "设置：" + AppPaths.SettingsFile + Environment.NewLine + "索引：" + AppPaths.IndexFile;

        LoadMonitors();
    }

    private void LoadMonitors()
    {
        _monitorChoices.Clear();
        _monitorChoices.Add(new MonitorChoice("跟随鼠标所在屏幕", MonitorPreference.Cursor, null));
        _monitorChoices.Add(new MonitorChoice("主显示器", MonitorPreference.Primary, null));
        _monitorChoices.Add(new MonitorChoice("当前窗口所在屏幕", MonitorPreference.FocusedWindow, null));
        foreach (var monitor in MonitorService.GetMonitors())
            _monitorChoices.Add(new MonitorChoice(monitor.Label, MonitorPreference.Fixed, monitor.DeviceName));

        MonitorCombo.ItemsSource = null;
        MonitorCombo.ItemsSource = _monitorChoices;
        var index = _monitorChoices.FindIndex(choice =>
            choice.Mode == _settings.PreferredMonitor
            && (choice.Mode != MonitorPreference.Fixed
                || string.Equals(choice.DeviceName, _settings.PreferredMonitorDevice, StringComparison.OrdinalIgnoreCase)));
        MonitorCombo.SelectedIndex = index >= 0 ? index : 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyService.TryParse(HotkeyBox.Text, out var hotkeyModifiers, out var hotkeyKey))
        {
            HotkeyStatusText.Text = "快捷键格式无效，例如 Alt+Space。";
            SettingsTabs.SelectedIndex = 4;
            return;
        }
        // 单字母/数字全局热键会吞掉正常输入；允许 F1–F24 这类安全单键。
        if (hotkeyModifiers == 0 && (hotkeyKey < 0x70 || hotkeyKey > 0x87))
        {
            HotkeyStatusText.Text = "全局快捷键至少需要一个修饰键（Ctrl / Alt / Shift / Win），或使用 F1–F24。";
            SettingsTabs.SelectedIndex = 4;
            return;
        }

        if (!int.TryParse(MaxResultsBox.Text.Trim(), out var maxResults)) maxResults = 50;
        if (!int.TryParse(MaxDepthBox.Text.Trim(), out var maxDepth)) maxDepth = 6;

        var extensions = BuildExtensions();
        if (extensions.Count == 0)
        {
            MessageBox.Show(this, "至少保留一种扫描文件类型，否则索引会为空。", "cTools",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SettingsTabs.SelectedIndex = 2;
            return;
        }

        ApplyControlsToSettings(maxResults, maxDepth, extensions);
        AutoStart.SetEnabled(_settings.StartWithWindows);
        _viewModel.ApplySettings(_settings);
        // 保存后保持设置页打开，方便继续调整；关闭由“关闭”按钮或标题栏完成。
        _previewed = false;
        _hasSaved = true;
        CancelButton.Content = "关闭";
        HotkeyStatusText.Text = "";
        ApplyWindowTheme();
        ShowSavedFeedback();
    }

    /// <summary>把当前表单写回临时设置，供保存和“按当前表单重建索引”共用。</summary>
    private void ApplyControlsToSettings(int? maxResults = null, int? maxDepth = null, List<string>? extensions = null)
    {
        _settings.MaxResults = Math.Clamp(maxResults ?? (int.TryParse(MaxResultsBox.Text.Trim(), out var results) ? results : 50), 5, 200);
        _settings.Hotkey = HotkeyBox.Text.Trim();
        _settings.Theme = ThemeDarkRadio.IsChecked == true
            ? AppTheme.Dark
            : ThemeLightRadio.IsChecked == true
                ? AppTheme.Light
                : AppTheme.System;
        _settings.ResultLayout = LayoutGridRadio.IsChecked == true ? ResultLayout.Grid : ResultLayout.List;
        _settings.PalettePosition = PosTopRadio.IsChecked == true ? PalettePosition.Top
            : PosCenterRadio.IsChecked == true ? PalettePosition.Center
            : PosLowerRadio.IsChecked == true ? PalettePosition.LowerCenter
            : PosBottomRadio.IsChecked == true ? PalettePosition.Bottom
            : PalettePosition.UpperCenter;
        _settings.EscapeBehavior = EscHideRadio.IsChecked == true ? EscapeBehavior.AlwaysHide : EscapeBehavior.ClearThenHide;
        _settings.SpaceToLaunch = SpaceToLaunchBox.IsChecked == true;
        _settings.SuppressHotkeyInFullscreen = SuppressFullscreenBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
        _settings.ShowTrayIcon = ShowTrayBox.IsChecked == true;
        _settings.HideOnBlur = HideOnBlurBox.IsChecked == true;
        _settings.SelectSearchTextOnOpen = SelectSearchTextBox.IsChecked == true;
        _settings.KeepSearchText = KeepSearchTextBox.IsChecked == true;
        _settings.CompactMode = CompactModeBox.IsChecked == true;
        _settings.Search.MaxScanDepth = Math.Clamp(
            maxDepth ?? (int.TryParse(MaxDepthBox.Text.Trim(), out var depth) ? depth : 6), 1, 12);

        if (MonitorCombo.SelectedItem is MonitorChoice monitor)
        {
            _settings.PreferredMonitor = monitor.Mode;
            _settings.PreferredMonitorDevice = monitor.DeviceName;
        }

        _settings.Search.StartMenu = StartMenuBox.IsChecked == true;
        _settings.Search.Desktop = DesktopBox.IsChecked == true;
        _settings.Search.AppPaths = AppPathsBox.IsChecked == true;
        _settings.Search.Uwp = UwpBox.IsChecked == true;
        _settings.Search.SystemSettings = SystemSettingsBox.IsChecked == true;
        _settings.Search.CustomEntries = CustomEntriesBox.IsChecked == true;
        _settings.Search.CustomPaths = CustomPathsBox.IsChecked == true;
        _settings.Search.IncludeSubfolders = IncludeSubfoldersBox.IsChecked == true;
        var collectedExtensions = extensions ?? BuildExtensions();
        if (collectedExtensions.Count > 0) _settings.ScanExtensions = collectedExtensions;
        _settings.ExcludePatterns = ExcludePatternsBox.Text
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> BuildExtensions()
    {
        var extensions = new List<string>();
        void Add(bool enabled, params string[] values)
        {
            if (!enabled) return;
            extensions.AddRange(values.Select(EntryKindDetector.NormalizeExtension).Where(value => value.Length > 1));
        }

        Add(ScanExeBox.IsChecked == true, ".exe");
        Add(ScanLnkBox.IsChecked == true, ".lnk");
        Add(ScanUrlBox.IsChecked == true, ".url");
        Add(ScanAppRefBox.IsChecked == true, ".appref-ms");
        Add(ScanBatBox.IsChecked == true, ".bat", ".cmd");
        Add(ScanPs1Box.IsChecked == true, ".ps1");
        Add(ScanMsiBox.IsChecked == true, ".msi");

        foreach (var raw in ExtraExtensionsBox.Text.Split(new[] { ',', ';', ' ', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = EntryKindDetector.NormalizeExtension(raw);
            if (extension.Length > 1) extensions.Add(extension);
        }
        return extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasSaved) DialogResult = false;
        Close();
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "Alt+Space";
        HotkeyStatusText.Text = "";
        HotkeyBox.Focus();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog { Description = "选择要扫描的文件夹" };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        AddScanPath(dialog.SelectedPath);
    }

    private void AddScanFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要加入扫描范围的文件",
            Filter = "所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == true) AddScanPath(dialog.FileName);
    }

    private void AddScanPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_settings.CustomPaths.Any(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        _settings.CustomPaths.Add(new CustomPath
        {
            Path = path,
            Recursive = Directory.Exists(path),
            Enabled = true
        });
        CustomPathsBox.IsChecked = true;
        RefreshCustomPaths();
    }

    private void RemovePath_Click(object sender, RoutedEventArgs e)
    {
        if (CustomPathsList.SelectedItem is not CustomPath path) return;
        _settings.CustomPaths.Remove(path);
        RefreshCustomPaths();
    }

    private void CustomList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CustomPathsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths) AddScanPath(path);
        e.Handled = true;
    }

    private void RefreshCustomPaths()
    {
        CustomPathsList.ItemsSource = null;
        CustomPathsList.ItemsSource = _settings.CustomPaths;
        UpdatePathsCount();
    }

    private void UpdatePathsCount()
    {
        var folders = _settings.CustomPaths.Count(path => path.Enabled && Directory.Exists(path.Path));
        var files = _settings.CustomPaths.Count(path => path.Enabled && File.Exists(path.Path));
        CustomPathsCountText.Text = $"{_settings.CustomPaths.Count} 个路径 · {folders} 个文件夹 · {files} 个文件";
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加的文件",
            Filter = "所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var name = EntryKindDetector.DefaultNameFor(dialog.FileName);
        AddCustomEntry(new CustomEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(dialog.FileName) : name,
            Target = dialog.FileName,
            Kind = EntryKindDetector.Infer(dialog.FileName)
        });
    }

    private void AddFolderEntry_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog { Description = "选择文件夹" };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        var name = EntryKindDetector.DefaultNameFor(dialog.SelectedPath);
        AddCustomEntry(new CustomEntry
        {
            Name = name,
            Target = dialog.SelectedPath,
            Kind = EntryKind.Folder
        });
    }

    private void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        AddCustomEntry(new CustomEntry { Name = "新网址", Target = "https://", Kind = EntryKind.Url });
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e)
    {
        AddCustomEntry(new CustomEntry { Kind = EntryKind.Command });
    }

    private void AddCustomEntry(CustomEntry entry)
    {
        var dialog = new CustomEntryWindow(entry) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings.CustomEntries.Add(dialog.Entry);
        CustomEntriesBox.IsChecked = true;
        RefreshCustomEntries();
    }

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (CustomEntriesList.SelectedItem is not CustomEntry entry) return;
        var dialog = new CustomEntryWindow(entry) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var index = _settings.CustomEntries.IndexOf(entry);
        if (index >= 0) _settings.CustomEntries[index] = dialog.Entry;
        RefreshCustomEntries();
    }

    private void EditEntry_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CustomEntriesList.SelectedItem is CustomEntry) EditEntry_Click(sender, e);
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (CustomEntriesList.SelectedItem is not CustomEntry entry) return;
        _settings.CustomEntries.Remove(entry);
        RefreshCustomEntries();
    }

    private void CustomEntriesList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (_settings.CustomEntries.Any(entry => string.Equals(entry.Target, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var name = EntryKindDetector.DefaultNameFor(path);
            _settings.CustomEntries.Add(new CustomEntry
            {
                Name = string.IsNullOrWhiteSpace(name) ? path : name,
                Target = path,
                Kind = EntryKindDetector.Infer(path),
                Enabled = true
            });
        }
        RefreshCustomEntries();
        CustomEntriesBox.IsChecked = true;
        e.Handled = true;
    }

    private void RefreshCustomEntries()
    {
        CustomEntriesList.ItemsSource = null;
        CustomEntriesList.ItemsSource = _settings.CustomEntries;
    }

    private async void Rebuild_Click(object sender, RoutedEventArgs e)
    {
        RebuildButton.IsEnabled = false;
        ScanStatusText.Text = "正在按当前表单重建索引…";
        try
        {
            ApplyControlsToSettings();
            _previewed = true;
            await _viewModel.RebuildIndexAsync(_settings.Clone(), persistCache: false);
            ScanStatusText.Text = _viewModel.Status + " · 尚未保存设置";
            ScanSummaryText.Text = _viewModel.ScanSummary;
        }
        finally
        {
            RebuildButton.IsEnabled = true;
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc 保留给“取消设置”，Tab 保留给焦点切换，不作为单键快捷键录制。
        if (e.Key is Key.Escape or Key.Tab) return;

        if (e.Key is Key.Back or Key.Delete)
        {
            HotkeyBox.Text = "";
            HotkeyStatusText.Text = "";
            e.Handled = true;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;
        parts.Add(HotkeyService.FormatKey(key));
        HotkeyBox.Text = string.Join("+", parts);
        HotkeyBox.CaretIndex = HotkeyBox.Text.Length;
        HotkeyStatusText.Text = "";
        e.Handled = true;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataRoot) { UseShellExecute = true });
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var target = File.Exists(AppPaths.LogFile) ? AppPaths.LogFile : Path.GetDirectoryName(AppPaths.LogFile)!;
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }
}
