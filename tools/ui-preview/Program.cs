using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using cTools.App;
using cTools.App.Services;
using cTools.App.ViewModels;
using cTools.Core.Models;
using cTools.Core.Storage;
using ThemeMode = cTools.Core.Models.ThemeMode;
using TabControl = System.Windows.Controls.TabControl;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace UiPreview;

/// <summary>
/// 不依赖桌面窗口的离屏渲染工具：实例化真实的 MainWindow / SettingsWindow，
/// 布局后用 RenderTargetBitmap 输出 PNG，便于在无交互桌面的环境检查界面。
/// 用法：dotnet run --project tools/ui-preview -- [输出目录]
/// </summary>
internal static class Program
{
    private const int MainWidth = 780;
    private const int MainHeight = 540;
    private const int SettingsWidth = 1000;
    private const int SettingsHeight = 720;

    [STAThread]
    private static void Main(string[] args)
    {
        var outputArgument = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        var output = outputArgument is not null
            ? Path.GetFullPath(outputArgument)
            : Path.Combine(Environment.CurrentDirectory, "artifacts", "ui");
        Directory.CreateDirectory(output);

        var app = new cTools.App.App();
        app.InitializeComponent();
        ThemeService.Apply(ThemeMode.Light);

        var viewModel = new MainViewModel(new AppSettings());
        SeedSettings(viewModel);
        SeedResults(viewModel);

        var main = new MainWindow(viewModel);
        main.ApplySettingsToWindow();
        Render(main, MainWidth, MainHeight, Path.Combine(output, "main-list-light.png"));

        var settings = new SettingsWindow(viewModel, 1);
        InvokeLoadControls(settings);
        RenderSettingsTab(settings, 1, Path.Combine(output, "settings-layout-light.png"));
        RenderSettingsTab(settings, 0, Path.Combine(output, "settings-general-light.png"));
        RenderSettingsTab(settings, 2, Path.Combine(output, "settings-scope-light.png"));
        RenderSettingsTab(settings, 3, Path.Combine(output, "settings-hotkey-light.png"));
        RenderSettingsTab(settings, 4, Path.Combine(output, "settings-clipboard-light.png"));
        RenderSettingsTab(settings, 5, Path.Combine(output, "settings-about-light.png"));

        var editor = new CustomEntryWindow(viewModel.Settings.CustomEntries[0]);
        Render(editor, 640, 680, Path.Combine(output, "custom-editor-light.png"));

        var emptyMain = new MainWindow(new MainViewModel(new AppSettings()));
        emptyMain.ApplySettingsToWindow();
        Render(emptyMain, MainWidth, MainHeight, Path.Combine(output, "main-empty-light.png"));

        var compactViewModel = new MainViewModel(new AppSettings());
        SeedResults(compactViewModel);
        SetSettings(compactViewModel, new AppSettings { CompactMode = true, Theme = ThemeMode.Light });
        var compactMain = new MainWindow(compactViewModel);
        compactMain.ApplySettingsToWindow();
        Render(compactMain, MainWidth, 72, Path.Combine(output, "main-compact-light.png"));

        SetSettings(viewModel, new AppSettings { ResultLayout = ResultLayout.Grid, Theme = ThemeMode.Light });
        main.ApplySettingsToWindow();
        Render(main, MainWidth, MainHeight, Path.Combine(output, "main-grid-light.png"));

        ThemeService.Apply(ThemeMode.Dark);
        SetSettings(viewModel, new AppSettings { ResultLayout = ResultLayout.Grid, Theme = ThemeMode.Dark });
        main.ApplySettingsToWindow();
        Render(main, MainWidth, MainHeight, Path.Combine(output, "main-grid-dark.png"));

        SetSettings(viewModel, new AppSettings { ResultLayout = ResultLayout.List, Theme = ThemeMode.Dark });
        SeedSettings(viewModel);
        main.ApplySettingsToWindow();
        Render(main, MainWidth, MainHeight, Path.Combine(output, "main-list-dark.png"));

        var darkSettings = new SettingsWindow(viewModel, 1);
        InvokeLoadControls(darkSettings);
        RenderSettingsTab(darkSettings, 1, Path.Combine(output, "settings-layout-dark.png"));

        Console.WriteLine("UI preview written to " + output);
    }

    private static void SeedSettings(MainViewModel viewModel)
    {
        viewModel.Settings.CustomPaths.Add(new CustomPath { Path = @"D:\Games", Recursive = true, Enabled = true });
        viewModel.Settings.CustomPaths.Add(new CustomPath { Path = @"D:\Tools\portable.exe", Recursive = false, Enabled = true });
        viewModel.Settings.CustomEntries.Add(new CustomEntry
        {
            Name = "项目文档",
            Target = @"D:\Work\Docs",
            Kind = EntryKind.Folder,
            Keywords = new List<string> { "docs", "工作" }
        });
        viewModel.Settings.CustomEntries.Add(new CustomEntry
        {
            Name = "uTools 官网",
            Target = "https://u.tools",
            Kind = EntryKind.Url,
            Enabled = true
        });
    }

    private static void SeedResults(MainViewModel viewModel)
    {
        var cached = IndexCache.Load() ?? new List<AppEntry>();
        var wanted = new[]
        {
            "Visual Studio Code", "Google Chrome", "微信", "Windows Terminal Launcher Shim",
            "7-Zip File Manager", "Aegisub subtitle editor", "DingTalk", "Doubao",
            "Microsoft Edge", "Microsoft OneNote", "Windows PowerShell", "计算器", "截图工具"
        };
        var picked = new List<AppEntry>();
        foreach (var name in wanted)
        {
            var match = cached.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null && picked.All(item => item.Id != match.Id))
                picked.Add(match.Clone());
        }
        foreach (var entry in cached.Where(entry => entry.Kind == EntryKind.App && File.Exists(entry.Target)))
        {
            if (picked.Count >= 10) break;
            if (picked.Any(item => item.Id == entry.Id)) continue;
            picked.Add(entry.Clone());
        }
        if (picked.Count == 0)
        {
            var fallback = new[]
            {
                ("Visual Studio Code", @"D:\Program Files\Microsoft VS Code\Code.exe"),
                ("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
                ("Windows PowerShell", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
                ("命令提示符", @"C:\Windows\System32\cmd.exe"),
                ("计算器", @"C:\Windows\System32\calc.exe"),
                ("任务管理器", @"C:\Windows\System32\Taskmgr.exe"),
                ("控制面板", @"C:\Windows\System32\control.exe"),
                ("文件资源管理器", @"C:\Windows\explorer.exe")
            };
            foreach (var (name, target) in fallback)
            {
                if (!File.Exists(target)) continue;
                picked.Add(new AppEntry
                {
                    Name = name,
                    Target = target,
                    ResolvedTarget = target,
                    IconPath = target,
                    Kind = EntryKind.App,
                    Source = EntrySource.AppPaths,
                    Description = target
                });
            }
        }
        if (picked.Count > 0) picked[0].Pinned = true;
        if (picked.Count > 6) picked[6].RunAsAdmin = true;

        foreach (var entry in picked.Take(12))
        {
            entry.EnsureId();
            // 离屏渲染时同步取图标，避免依赖 Dispatcher 异步回调。
            var item = new ResultItem(entry)
            {
                Icon = IconService.GetIcon(entry.IconPath ?? entry.ResolvedTarget ?? entry.Target)
            };
            viewModel.Results.Add(item);
        }
        viewModel.Selected = viewModel.Results.FirstOrDefault();
    }

    private static void SetSettings(MainViewModel viewModel, AppSettings settings)
    {
        var field = typeof(MainViewModel).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(viewModel, settings);
    }

    private static void InvokeLoadControls(SettingsWindow window)
    {
        var method = typeof(SettingsWindow).GetMethod("LoadControls", BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(window, null);
    }

    private static void RenderSettingsTab(SettingsWindow window, int tabIndex, string path)
    {
        if (window.FindName("SettingsTabs") is TabControl tabs) tabs.SelectedIndex = tabIndex;
        Render(window, SettingsWidth, SettingsHeight, path);
    }

    /// <summary>等待模板中的入场 / 补间动画走完，避免离屏截图停在动画中间帧。</summary>
    private static void SettleAnimations()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(520)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Render(Window window, int width, int height, string path)
    {
        if (window.Content is not FrameworkElement root) return;

        if (window is MainWindow && window.FindName("RootBorder") is Border border)
        {
            root = border;
            border.Effect = null;
        }

        root.Width = width;
        root.Height = height;
        if (root is System.Windows.Controls.Panel panel && panel.Background is null)
            panel.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBgBrush"];
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        // 模板里的颜色补间需要真实的 dispatcher 时间才会走到终态；否则离屏截图会停在中间帧。
        SettleAnimations();
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        if (window is MainWindow)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var background = new LinearGradientBrush(
                    Color.FromRgb(203, 213, 225),
                    Color.FromRgb(241, 245, 249),
                    new Point(0, 0),
                    new Point(1, 1));
                context.DrawRectangle(background, null, new Rect(0, 0, width, height));
                var brush = new VisualBrush(root)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }
            bitmap.Render(visual);
        }
        else
        {
            bitmap.Render(root);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine(path);
    }
}
