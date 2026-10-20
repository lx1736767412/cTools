using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using cTools.App.Services;
using cTools.App.ViewModels;
using cTools.Core.Models;
using cTools.Core.Scanning;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

namespace cTools.App;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    private const double PaletteWidth = 780;
    private const double PaletteHeight = 540;
    private const double CompactHeight = 72;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _searchTimer;
    private readonly DataTemplate _listItemTemplate;
    private readonly ItemsPanelTemplate _listItemsPanel;
    private readonly Style _listItemContainerStyle;
    private IntPtr _previousForeground;
    private SettingsWindow? _settingsWindow;
    private bool _suppressHide;
    private bool _isExiting;
    private long _ignoreDeactivateUntil;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _listItemTemplate = ResultsList.ItemTemplate;
        _listItemsPanel = ResultsList.ItemsPanel;
        _listItemContainerStyle = ResultsList.ItemContainerStyle;
        _viewModel.Results.CollectionChanged += (_, _) => UpdateEmptyState();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _viewModel.Query = SearchBox.Text;
            if (_viewModel.Results.Count > 0) ResultsList.SelectedIndex = 0;
            UpdateEmptyState();
        };
        SourceInitialized += (_, _) => ThemeService.Apply(_viewModel.Settings.Theme, this);
        Loaded += (_, _) =>
        {
            ApplyResultLayout();
            UpdateHints();
            UpdateEmptyState();
        };
    }

    public void EnsureHandle()
    {
        new WindowInteropHelper(this).EnsureHandle();
    }

    public void SetQuery(string text)
    {
        SearchBox.Text = text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        _viewModel.Query = text;
    }

    public void ShowPalette()
    {
        _previousForeground = GetForegroundWindow();
        RefreshSuggestions();
        _ignoreDeactivateUntil = Environment.TickCount64 + 700;
        ApplyCompactState();
        PositionWindow();
        if (!IsVisible) Show();
        Topmost = true;
        Activate();
        ForceForeground();
        SearchBox.Focus();
        if (_viewModel.Settings.SelectSearchTextOnOpen)
            SearchBox.SelectAll();
        else
            SearchBox.CaretIndex = SearchBox.Text.Length;
        if (_viewModel.Results.Count > 0)
            ResultsList.SelectedIndex = 0;
        UpdateEmptyState();
        PlayShowAnimation();
    }

    /// <summary>读取剪贴板和资源管理器当前文件夹，生成空查询时排在最前面的快捷操作。</summary>
    private void RefreshSuggestions()
    {
        var suggestions = new List<ResultItem>();
        var editor = _viewModel.Settings.TextEditorPath;
        var hasEditor = !string.IsNullOrWhiteSpace(editor) && File.Exists(editor);

        var clipboardPath = ReadClipboardPath();
        if (clipboardPath is not null)
        {
            if (Directory.Exists(clipboardPath))
            {
                suggestions.Add(CreateSuggestion("添加至自定义启动", clipboardPath,
                    () => _viewModel.AddCustomEntryFromPath(clipboardPath)));
                suggestions.Add(CreateSuggestion("添加至自定义扫描路径", clipboardPath,
                    () => _viewModel.AddScanPathFromPath(clipboardPath)));
            }
            else if (File.Exists(clipboardPath))
            {
                suggestions.Add(CreateSuggestion("添加至自定义启动", clipboardPath,
                    () => _viewModel.AddCustomEntryFromPath(clipboardPath)));
                if (hasEditor)
                    suggestions.Add(CreateEditorSuggestion("使用文本编辑器打开", clipboardPath, editor!));
            }
        }

        var focusedFolder = GetExplorerFolder(_previousForeground);
        if (focusedFolder is not null
            && !string.Equals(focusedFolder, clipboardPath, StringComparison.OrdinalIgnoreCase)
            && hasEditor
            && TextEditorCatalog.SupportsFolders(editor))
            suggestions.Add(CreateEditorSuggestion("使用文本编辑器打开当前文件夹", focusedFolder, editor!));

        _viewModel.SetSuggestions(suggestions);
    }

    private static ResultItem CreateSuggestion(string title, string path, Action action)
    {
        var entry = new AppEntry
        {
            Name = title,
            Target = path,
            ResolvedTarget = path,
            Description = path,
            Kind = EntryKind.Command,
            Source = EntrySource.Manual
        };
        return new ResultItem(entry, action);
    }

    private ResultItem CreateEditorSuggestion(string title, string path, string editorPath)
    {
        var entry = new AppEntry
        {
            Name = title,
            Target = path,
            ResolvedTarget = path,
            Description = path,
            Kind = EntryKind.Command,
            Source = EntrySource.Manual,
            IconPath = editorPath
        };
        return new ResultItem(entry, () => OpenWithEditor(editorPath, path));
    }

    private void OpenWithEditor(string editorPath, string targetPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = editorPath,
                Arguments = $"\"{targetPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _viewModel.Status = "打开编辑器失败：" + ex.Message;
        }
    }

    private static string? ReadClipboardPath()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return null;
            foreach (var path in Clipboard.GetFileDropList())
            {
                if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                    return path;
            }
        }
        catch
        {
            // 剪贴板被其他程序占用时忽略。
        }
        return null;
    }

    /// <summary>如果呼出前的前台窗口是资源管理器文件夹，返回它的本地路径。</summary>
    private static string? GetExplorerFolder(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            GetWindowThreadProcessId(hwnd, out var processId);
            using var process = Process.GetProcessById((int)processId);
            if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)) return null;

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            object? shell = null;
            object? windows = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell is null) return null;
                dynamic dynamicShell = shell;
                windows = dynamicShell.Windows();
                if (windows is null) return null;

                dynamic dynamicWindows = windows;
                foreach (dynamic window in dynamicWindows)
                {
                    try
                    {
                        if ((long)window.HWND != hwnd.ToInt64()) continue;
                        var url = (string?)window.LocationURL;
                        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var path = new Uri(url).LocalPath;
                        return Directory.Exists(path) ? path : null;
                    }
                    finally
                    {
                        if (window is not null && Marshal.IsComObject(window)) Marshal.FinalReleaseComObject(window);
                    }
                }
            }
            finally
            {
                if (windows is not null && Marshal.IsComObject(windows)) Marshal.FinalReleaseComObject(windows);
                if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            // 资源管理器未运行或 COM 不可用时忽略。
        }
        return null;
    }

    public void HidePalette(bool restoreFocus = true)    {
        Hide();
        if (!_viewModel.Settings.KeepSearchText)
        {
            SearchBox.Text = string.Empty;
            _viewModel.Query = string.Empty;
        }
        if (restoreFocus && _previousForeground != IntPtr.Zero)
        {
            SetForegroundWindow(_previousForeground);
            _previousForeground = IntPtr.Zero;
        }
    }

    public void OpenSettings(int initialTab = 0)
    {
        _suppressHide = true;
        try
        {
            var ownerVisible = IsVisible;
            _settingsWindow = new SettingsWindow(_viewModel, initialTab)
            {
                Owner = ownerVisible ? this : null,
                WindowStartupLocation = ownerVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = !ownerVisible
            };
            _settingsWindow.ShowDialog();
        }
        finally
        {
            _settingsWindow = null;
            _suppressHide = false;
            ApplySettingsToWindow();
            SearchBox.Focus();
        }
    }

    /// <summary>设置保存后立即应用布局、尺寸与提示文案，无需重启。</summary>
    public void ApplySettingsToWindow()
    {
        ApplyResultLayout();
        ApplyCompactState();
        UpdateHints();
        UpdateEmptyState();
    }

    public void CloseForExit()
    {
        _isExiting = true;
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_suppressHide || _isExiting || _settingsWindow is not null) return;
        // 呼出瞬间 Windows 可能先派发一次 Deactivated，避免窗口刚显示就被隐藏。
        if (Environment.TickCount64 < _ignoreDeactivateUntil) return;
        if (_viewModel.Settings.HideOnBlur) HidePalette();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting) return;
        e.Cancel = true;
        HidePalette();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SearchBox.IsKeyboardFocusWithin) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Button && e.Key is not Key.Escape) return;
        switch (e.Key)
        {
            case Key.Escape:
                HandleEscape();
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1, false);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1, false);
                e.Handled = true;
                break;
            case Key.Left when _viewModel.Results.Count > 0:
                MoveSelection(-1, IsGridLayout);
                e.Handled = true;
                break;
            case Key.Right when _viewModel.Results.Count > 0:
                MoveSelection(1, IsGridLayout);
                e.Handled = true;
                break;
            case Key.Enter:
            {
                var admin = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                var location = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                TryLaunchSelected(admin, location);
                e.Handled = true;
                break;
            }
            case Key.Space when _viewModel.Settings.SpaceToLaunch:
                TryLaunchSelected(false, false);
                e.Handled = true;
                break;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyCompactState();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HandleEscape();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1, false);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1, false);
                e.Handled = true;
                break;
            case Key.Left when _viewModel.Results.Count > 0:
                MoveSelection(-1, IsGridLayout);
                e.Handled = true;
                break;
            case Key.Right when _viewModel.Results.Count > 0:
                MoveSelection(1, IsGridLayout);
                e.Handled = true;
                break;
            case Key.PageDown:
                MoveSelection(IsGridLayout ? 3 : 8, false);
                e.Handled = true;
                break;
            case Key.PageUp:
                MoveSelection(IsGridLayout ? -3 : -8, false);
                e.Handled = true;
                break;
            case Key.Home when string.IsNullOrEmpty(SearchBox.Text):
                SelectIndex(0);
                e.Handled = true;
                break;
            case Key.End when string.IsNullOrEmpty(SearchBox.Text):
                SelectIndex(_viewModel.Results.Count - 1);
                e.Handled = true;
                break;
            case Key.Enter:
            {
                var admin = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                var location = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                TryLaunchSelected(admin, location);
                e.Handled = true;
                break;
            }
            case Key.Space when _viewModel.Settings.SpaceToLaunch && Keyboard.Modifiers == ModifierKeys.None:
                TryLaunchSelected(false, false);
                e.Handled = true;
                break;
        }
    }

    private void HandleEscape()
    {
        if (_viewModel.Settings.EscapeBehavior == EscapeBehavior.AlwaysHide || string.IsNullOrEmpty(SearchBox.Text))
        {
            HidePalette();
            return;
        }
        SearchBox.Text = string.Empty;
        _viewModel.Query = string.Empty;
    }

    private bool TryLaunchSelected(bool admin, bool openLocation)
    {
        if (!_viewModel.LaunchSelected(admin, openLocation)) return false;
        HidePalette(false);
        return true;
    }

    private bool IsGridLayout => _viewModel.Settings.ResultLayout == ResultLayout.Grid;

    private void MoveSelection(int delta, bool horizontal)
    {
        var count = _viewModel.Results.Count;
        if (count == 0) return;
        var index = ResultsList.SelectedIndex;
        if (index < 0) index = 0;

        if (IsGridLayout)
        {
            var step = horizontal ? delta : delta * GridColumnCount();
            index = Math.Clamp(index + step, 0, count - 1);
        }
        else
        {
            index += delta;
            if (index < 0) index = count - 1;
            if (index >= count) index = 0;
        }

        ResultsList.SelectedIndex = index;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void SelectIndex(int index)
    {
        if (_viewModel.Results.Count == 0) return;
        ResultsList.SelectedIndex = Math.Clamp(index, 0, _viewModel.Results.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private int GridColumnCount()
    {
        var width = ResultsList.ActualWidth > 0 ? ResultsList.ActualWidth : PaletteWidth - 20;
        return Math.Max(1, (int)((width - 16) / 92));
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.Selected = ResultsList.SelectedItem as ResultItem;
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Settings.LaunchOnSingleClick) return;
        if (_viewModel.Selected is null) return;
        TryLaunchSelected(false, false);
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.Settings.LaunchOnSingleClick) return;
        if (e.OriginalSource is not DependencyObject source) return;
        var item = FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is not ResultItem result) return;
        ResultsList.SelectedItem = result;
        TryLaunchSelected(false, false);
        e.Handled = true;
    }

    private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var item = FindAncestor<ListBoxItem>(source);
            if (item is not null) ResultsList.SelectedItem = item.DataContext;
        }
        ShowContextMenu();
        e.Handled = true;
    }

    private void ShowContextMenu()
    {
        if (_viewModel.Selected is null) return;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开 (Enter)" };
        open.Click += (_, _) => TryLaunchSelected(false, false);
        var admin = new MenuItem { Header = "以管理员身份打开 (Ctrl+Enter)" };
        admin.Click += (_, _) => TryLaunchSelected(true, false);
        var location = new MenuItem { Header = "打开所在位置 (Shift+Enter)" };
        location.Click += (_, _) => TryLaunchSelected(false, true);
        location.IsEnabled = _viewModel.Selected.Entry.Kind is not (EntryKind.Url or EntryKind.Uwp or EntryKind.SystemSetting);
        var copy = new MenuItem { Header = "复制路径" };
        copy.Click += (_, _) =>
        {
            var path = _viewModel.Selected?.Entry.ResolvedTarget ?? _viewModel.Selected?.Entry.Target;
            if (!string.IsNullOrWhiteSpace(path)) Clipboard.SetText(path);
        };
        var pin = new MenuItem { Header = _viewModel.Selected.Entry.Pinned ? "取消固定" : "固定到顶部" };
        pin.Click += (_, _) => _viewModel.TogglePin();
        menu.Items.Add(open);
        menu.Items.Add(admin);
        menu.Items.Add(location);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(pin);
        if (_viewModel.Selected.Entry.Source is EntrySource.CustomEntry or EntrySource.CustomPath)
        {
            var remove = new MenuItem { Header = "从自定义中删除" };
            remove.Click += (_, _) => _viewModel.RemoveSelectedCustom();
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
        }
        menu.PlacementTarget = ResultsList;
        menu.IsOpen = true;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void AddFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添加到 cTools",
            Filter = "所有文件|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        _viewModel.AddPaths(dialog.FileNames);
        _viewModel.Refresh();
        UpdateEmptyState();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddPaths(paths);
            _viewModel.Refresh();
            UpdateEmptyState();
        }
    }

    private void ApplyResultLayout()
    {
        if (IsGridLayout)
        {
            ResultsList.ItemsPanel = (ItemsPanelTemplate)FindResource("GridItemsPanel");
            ResultsList.ItemTemplate = (DataTemplate)FindResource("GridResultTemplate");
            ResultsList.ItemContainerStyle = (Style)FindResource("GridItemStyle");
        }
        else
        {
            ResultsList.ItemsPanel = _listItemsPanel;
            ResultsList.ItemTemplate = _listItemTemplate;
            ResultsList.ItemContainerStyle = _listItemContainerStyle;
        }
        if (_viewModel.Selected is not null)
            ResultsList.ScrollIntoView(_viewModel.Selected);
    }

    private void UpdateHints()
    {
        NavHintText.Text = IsGridLayout ? "←↑↓→ 选择" : "↑↓ 选择";
        SpaceHintChip.Visibility = _viewModel.Settings.SpaceToLaunch ? Visibility.Visible : Visibility.Collapsed;
        AdminHintChip.Visibility = Visibility.Visible;
    }

    private void UpdateEmptyState() => ApplyCompactState();

    private bool ShouldCollapseToSearchBox()
    {
        if (!string.IsNullOrEmpty(SearchBox.Text)) return false;
        // 没有历史记录时只保留一个搜索框；开启紧凑模式时同理。
        return _viewModel.Settings.CompactMode || _viewModel.Results.Count == 0;
    }

    private void ApplyCompactState()
    {
        var collapse = ShouldCollapseToSearchBox();
        if (collapse)
        {
            FooterRow.Height = new GridLength(0);
            ContentRow.Height = new GridLength(0);
            FooterBorder.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = Visibility.Collapsed;
        }
        else
        {
            FooterRow.Height = new GridLength(40);
            ContentRow.Height = new GridLength(1, GridUnitType.Star);
            FooterBorder.Visibility = Visibility.Visible;

            var empty = _viewModel.Results.Count == 0;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ResultsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty)
            {
                EmptyTitleText.Text = "没有找到匹配项";
                EmptyHintText.Text = "换个关键词，或把文件 / 文件夹直接拖进来添加";
            }
        }

        var targetHeight = DesiredHeight();
        var heightChanged = Math.Abs(Height - targetHeight) > 0.5;
        if (heightChanged) Height = targetHeight;
        if (IsVisible && heightChanged) PositionWindow();
    }

    /// <summary>按当前候选数量计算高度，最大不超过 PaletteHeight，没有候选时只保留搜索框。</summary>
    private double DesiredHeight()
    {
        if (ShouldCollapseToSearchBox()) return CompactHeight;

        var count = Math.Max(1, _viewModel.Results.Count);
        double contentHeight;
        if (IsGridLayout)
        {
            var width = Width > 0 ? Width : PaletteWidth;
            var columns = Math.Max(1, (int)((width - 28) / 92));
            var rows = (int)Math.Ceiling(count / (double)columns);
            contentHeight = rows * 92 + 8;
        }
        else
        {
            contentHeight = count * 58 + 8;
        }

        return Math.Clamp(72 + 40 + contentHeight, CompactHeight, PaletteHeight);
    }

    private void PositionWindow()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorService.GetTarget(_viewModel.Settings, hwnd);
            var work = monitor.WorkArea;
            var width = Math.Max(320, Math.Min(PaletteWidth, work.Width - 40));
            var height = DesiredHeight();
            height = Math.Max(CompactHeight, Math.Min(height, work.Height - 24));

            var top = _viewModel.Settings.PalettePosition switch
            {
                PalettePosition.Top => work.Top + 12,
                PalettePosition.UpperCenter => work.Top + 12 + Math.Max(0, work.Height - height - 24) * 0.18,
                PalettePosition.Center => work.Top + 12 + Math.Max(0, work.Height - height - 24) * 0.50,
                PalettePosition.LowerCenter => work.Top + 12 + Math.Max(0, work.Height - height - 24) * 0.78,
                PalettePosition.Bottom => work.Top + work.Height - height - 12,
                _ => work.Top + 12 + Math.Max(0, work.Height - height - 24) * 0.18
            };
            top = Math.Clamp(top, work.Top + 12, Math.Max(work.Top + 12, work.Top + work.Height - height - 12));

            Width = width;
            Height = height;
            Left = work.Left + (work.Width - width) / 2;
            Top = top;
        }
        catch
        {
            // 多显示器 API 失败时退回主屏居中。
            var width = PaletteWidth;
            var height = Height > 0 ? Height : PaletteHeight;
            Left = (SystemParameters.PrimaryScreenWidth - width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - height) / 2;
            Width = width;
        }
    }

    private void PlayShowAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            RootBorder.Opacity = 1;
            return;
        }
        try
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(150);
            RootBorder.BeginAnimation(OpacityProperty, null);
            RootBorder.Opacity = 0;
            RootBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            var transform = RootBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = -8;
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(-8, 0, duration) { EasingFunction = ease });
            RootBorder.RenderTransform = transform;
        }
        catch
        {
            RootBorder.Opacity = 1;
        }
    }

    private void ForceForeground()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var foreground = GetForegroundWindow();
            if (foreground == hwnd) return;
            var foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var currentThread = GetCurrentThreadId();
            AttachThreadInput(currentThread, foregroundThread, true);
            SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);
            AttachThreadInput(currentThread, foregroundThread, false);
        }
        catch
        {
            // 焦点抢占失败时仍保留 Activate 的结果。
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
