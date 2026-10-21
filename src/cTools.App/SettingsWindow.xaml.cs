using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using cTools.App.Services;
using cTools.App.ViewModels;
using cTools.Core.Models;
using cTools.Core.Scanning;
using cTools.Core.Storage;
using WinForms = System.Windows.Forms;
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using AppTheme = cTools.Core.Models.ThemeMode;

namespace cTools.App;

public partial class SettingsWindow : Window
{
    private static readonly string[] KnownExtensions =
    {
        ".exe", ".lnk"
    };

    public sealed record MonitorChoice(string Label, MonitorPreference Mode, string? DeviceName)
    {
        public override string ToString() => Label;
    }

    public sealed record EditorChoice(string Label, string Path)
    {
        public override string ToString() => Label;
    }

    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly List<MonitorChoice> _monitorChoices = new();
    private readonly List<EditorChoice> _editorChoices = new();
    private readonly DispatcherTimer _saveFeedbackTimer;
    private readonly DispatcherTimer _applyTimer;
    private bool _loading;
    private int _saveFeedbackVersion;

    public SettingsWindow(MainViewModel viewModel, int initialTab = 0)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = viewModel.Settings.Clone();
        SettingsTabs.SelectedIndex = Math.Clamp(initialTab, 0, 5);
        SettingsTabs.SelectionChanged += SettingsTabs_SelectionChanged;
        _saveFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _saveFeedbackTimer.Tick += (_, _) =>
        {
            _saveFeedbackTimer.Stop();
            HideSavedFeedback();
        };
        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _applyTimer.Tick += (_, _) =>
        {
            _applyTimer.Stop();
            ApplyNow();
        };

        // 所有控件改动都走同一套“防抖后立即生效”的流程，不再需要保存按钮。
        AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(LiveControlChanged), true);
        AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(LiveControlChanged), true);
        AddHandler(System.Windows.Controls.TextBox.TextChangedEvent, new TextChangedEventHandler(LiveControlChanged), true);
        AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler(LiveControlChanged), true);

        // 列表内部滚到边缘后，把滚轮交给外层页面，避免鼠标停在列表上时页面无法滚动。
        CustomEntriesList.PreviewMouseWheel += ForwardWheelToPage;
        CustomPathsList.PreviewMouseWheel += ForwardWheelToPage;

        SourceInitialized += (_, _) => ApplyWindowTheme();
        Loaded += (_, _) =>
        {
            LoadControls();
            PlayEntranceAnimation();
        };
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            _saveFeedbackTimer.Stop();
            _applyTimer.Stop();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    private void ApplyWindowTheme()
    {
        var dark = _settings.Theme == AppTheme.Dark
            || (_settings.Theme == AppTheme.System && ThemeService.IsSystemDark());
        WindowEffects.Apply(this, dark);
    }

    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 子列表的选中变化也会冒泡到这里，只处理标签页自身的切换。
        if (!ReferenceEquals(e.OriginalSource, SettingsTabs)) return;
        PlayTabTransition();
    }

    /// <summary>切换标签页时让右侧内容轻微淡入，避免整块内容瞬间替换。</summary>
    private void PlayTabTransition()
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        if (SettingsTabs.Template?.FindName("TabContentHost", SettingsTabs) is not ContentPresenter host) return;
        host.BeginAnimation(OpacityProperty, null);
        host.Opacity = 0.35;
        host.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void ShowSavedFeedback()
    {
        SaveStatusText.Text = "已自动保存";
        SaveStatusText.Visibility = Visibility.Visible;
        _saveFeedbackVersion++;
        if (SystemParameters.ClientAreaAnimation)
        {
            SaveStatusText.BeginAnimation(UIElement.OpacityProperty, null);
            SaveStatusText.Opacity = 0;
            SaveStatusText.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            SaveStatusText.Opacity = 1;
        }
        _saveFeedbackTimer.Stop();
        _saveFeedbackTimer.Start();
    }

    /// <summary>保存提示淡出后收起，避免底栏文字突兀消失。</summary>
    private void HideSavedFeedback()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            SaveStatusText.Visibility = Visibility.Collapsed;
            return;
        }

        var version = _saveFeedbackVersion;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (version != _saveFeedbackVersion) return;
            SaveStatusText.BeginAnimation(UIElement.OpacityProperty, null);
            SaveStatusText.Visibility = Visibility.Collapsed;
        };
        SaveStatusText.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>打开设置时让内容轻微淡入，与主面板的呼出动画保持一致。</summary>
    private void PlayEntranceAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation || Content is not FrameworkElement root) return;
        root.BeginAnimation(UIElement.OpacityProperty, null);
        root.Opacity = 0;
        root.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void LiveControlChanged(object sender, RoutedEventArgs e)
    {
        // 窗口级 AddHandler 的 sender 恒为 SettingsWindow，必须从路由事件的原始来源解析出具体开关。
        // 保留“用户正在操作这个开关”的门控：载入门控、列表批量赋值和虚拟化都不会让开关获得焦点或鼠标悬停。
        var box = FindToggleSource(e.OriginalSource);
        if (!_loading && box is not null && (box.IsKeyboardFocused || box.IsMouseOver))
            AnimateToggle(box, box.IsChecked == true);
        QueueApply();
    }

    /// <summary>从路由事件的原始来源（或它的视觉 / 逻辑祖先）向上找到开关。</summary>
    private static CheckBox? FindToggleSource(object? originalSource)
    {
        var node = originalSource as DependencyObject;
        while (node is not null)
        {
            if (node is CheckBox box) return box;
            node = node switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(node),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(node)
            };
        }
        return null;
    }

    /// <summary>
    /// 开关切换时把滑块与蓝色轨道补间到新状态；只动画 40×22 的开关，开销可忽略。
    /// </summary>
    private static void AnimateToggle(CheckBox box, bool isOn)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (box.Template is null) return;
        box.ApplyTemplate();
        if (box.Template.FindName("TrackOn", box) is not Border trackOn) return;
        if (box.Template.FindName("Thumb", box) is not UIElement thumb) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(160);
        var from = isOn ? 3.0 : 21.0;
        var to = isOn ? 21.0 : 3.0;

        thumb.BeginAnimation(Canvas.LeftProperty,
            new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        trackOn.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(isOn ? 0 : 1, isOn ? 1 : 0, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
    }

    private void QueueApply()
    {
        if (_loading) return;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void ApplyNow()
    {
        if (_loading) return;
        if (!HotkeyService.TryParse(HotkeyBox.Text, out var modifiers, out var key))
        {
            HotkeyStatusText.Text = "快捷键格式无效，例如 Alt+Space。";
            return;
        }
        if (modifiers == 0 && (key < 0x70 || key > 0x87))
        {
            HotkeyStatusText.Text = "全局快捷键至少需要一个修饰键（Ctrl / Alt / Shift / Win），或使用 F1–F24。";
            return;
        }

        HotkeyStatusText.Text = "";
        ApplyControlsToSettings();
        AutoStart.SetEnabled(_settings.StartWithWindows);
        _viewModel.ApplySettings(_settings);
        UpdateEditorStatus();
        ApplyWindowTheme();
        ShowSavedFeedback();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ScanSummary))
            ScanSummaryText.Text = _viewModel.ScanSummary;
    }

    private void LoadControls()
    {
        _loading = true;
        try
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
        SingleClickRadio.IsChecked = _settings.LaunchOnSingleClick;
        DoubleClickRadio.IsChecked = !_settings.LaunchOnSingleClick;
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
        VersionText.Text = "版本 " + UpdateService.CurrentVersionText;
        DataPathText.Text = "设置：" + AppPaths.SettingsFile + Environment.NewLine + "索引：" + AppPaths.IndexFile;
        AutoUpdateBox.IsChecked = _settings.AutoCheckUpdates;
        UpdateStatusText.Text = "当前版本 " + UpdateService.CurrentVersionText + "。";

        LoadMonitors();
        LoadEditors();
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadEditors()
    {
        TextEditorCatalog.EnsureConfigured(_settings);
        _editorChoices.Clear();
        foreach (var path in _settings.TextEditors)
            _editorChoices.Add(new EditorChoice(TextEditorCatalog.DisplayName(path), path));

        TextEditorCombo.ItemsSource = null;
        TextEditorCombo.ItemsSource = _editorChoices;
        var index = _editorChoices.FindIndex(choice =>
            string.Equals(choice.Path, _settings.TextEditorPath, StringComparison.OrdinalIgnoreCase));
        TextEditorCombo.SelectedIndex = index >= 0 ? index : (_editorChoices.Count > 0 ? 0 : -1);
        UpdateEditorStatus();
    }

    private void UpdateEditorStatus()
    {
        if (TextEditorCombo.SelectedItem is not EditorChoice choice)
        {
            EditorStatusText.Text = "未找到文本编辑器，请添加一个程序。";
            return;
        }
        EditorStatusText.Text = TextEditorCatalog.SupportsFolders(choice.Path)
            ? "当前编辑器支持直接打开文件夹。"
            : "当前编辑器不支持打开文件夹；打开文件夹时不会显示相关候选。";
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _applyTimer.Stop();
        ApplyNow();
        Close();
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
        _settings.LaunchOnSingleClick = SingleClickRadio.IsChecked == true;
        _settings.AutoCheckUpdates = AutoUpdateBox.IsChecked == true;
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
        if (TextEditorCombo.SelectedItem is EditorChoice editor)
            _settings.TextEditorPath = editor.Path;
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

        foreach (var raw in ExtraExtensionsBox.Text.Split(new[] { ',', ';', ' ', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = EntryKindDetector.NormalizeExtension(raw);
            if (extension.Length > 1) extensions.Add(extension);
        }
        return extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddEditor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择文本编辑器",
            Filter = "可执行文件|*.exe|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var path = Path.GetFullPath(dialog.FileName);
        if (!_settings.TextEditors.Contains(path, StringComparer.OrdinalIgnoreCase))
            _settings.TextEditors.Add(path);
        _settings.TextEditorPath = path;
        LoadEditors();
        QueueApply();
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
        QueueApply();
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
        QueueApply();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "清除所有搜索记录和最近使用排序？", "cTools",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _viewModel.ClearUsageHistory();
        ScanStatusText.Text = "已清除搜索记录";
        ScanSummaryText.Text = _viewModel.ScanSummary;
    }

    private async void Rebuild_Click(object sender, RoutedEventArgs e)
    {
        RebuildButton.IsEnabled = false;
        ScanStatusText.Text = "正在按当前设置重建索引…";
        try
        {
            _applyTimer.Stop();
            ApplyNow();
            await _viewModel.RebuildIndexAsync(_settings.Clone());
            ScanStatusText.Text = _viewModel.Status;
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

    private void ForwardWheelToPage(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source) return;
        var inner = FindDescendant<ScrollViewer>(source);
        var outer = FindAncestor<ScrollViewer>(source);
        if (inner is null || outer is null) return;

        var scrollingUp = e.Delta > 0;
        var atTop = inner.VerticalOffset <= 0.5;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
        if ((scrollingUp && atTop) || (!scrollingUp && atBottom))
        {
            outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta * 0.5);
            e.Handled = true;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataRoot) { UseShellExecute = true });
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        try
        {
            var found = await ((App)Application.Current).CheckForUpdatesAsync(manual: true);
            UpdateStatusText.Text = found ? "发现新版本，已打开更新窗口。" : "已是最新版本。";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "检查失败：" + ex.Message;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var target = File.Exists(AppPaths.LogFile) ? AppPaths.LogFile : Path.GetDirectoryName(AppPaths.LogFile)!;
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }
}
