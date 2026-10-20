using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using cTools.App.Services;
using cTools.Core.Logging;
using cTools.Core.Models;
using cTools.Core.Storage;
using cTools.Core.Updating;

namespace cTools.App;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly UpdateService _updates;
    private readonly AppSettings _settings;
    private readonly bool _canSelfUpdate;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public UpdateWindow(UpdateInfo update, UpdateService updates, AppSettings settings, bool canSelfUpdate)
    {
        InitializeComponent();
        _update = update;
        _updates = updates;
        _settings = settings;
        _canSelfUpdate = canSelfUpdate;

        Title = $"cTools 更新 · {update.Version.Semver}";
        VersionText.Text = $"{update.Version.Display}（当前 {UpdateService.CurrentVersionText}）";
        NotesText.Text = FormatNotes(update.Notes);
        if (string.IsNullOrWhiteSpace(NotesText.Text)) NotesCard.Visibility = Visibility.Collapsed;

        if (_canSelfUpdate)
        {
            StatusText.Text = $"已下载大小约 {FormatSize(update.Size)}。更新会关闭 cTools 并在安装完成后自动重新打开。";
        }
        else
        {
            SkipBox.Visibility = Visibility.Collapsed;
            InstallButton.Content = "打开发布页";
            StatusText.Text = "当前为便携版，自动安装只对安装版生效；请手动下载新的压缩包。";
        }

        SourceInitialized += (_, _) => WindowEffects.Apply(this, ThemeService.IsCurrentDark());
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_busy)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!_canSelfUpdate)
        {
            UpdateService.OpenReleasePage(_update);
            Close();
            return;
        }
        if (_busy) return;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            StatusText.Text = "正在下载更新… 0%";
            var progress = new Progress<double>(value =>
            {
                DownloadProgress.Value = Math.Clamp(value * 100, 0, 100);
                StatusText.Text = $"正在下载更新… {value:P0}";
            });

            var installer = await _updates.DownloadAsync(_update, progress, token);
            if (token.IsCancellationRequested) return;

            DownloadProgress.Value = 100;
            StatusText.Text = "下载完成，正在启动安装程序…";
            _updates.LaunchInstaller(installer);
            ((App)Application.Current).ExitForUpdate();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载。";
            SetBusy(false);
        }
        catch (Exception ex)
        {
            Log.Error("下载或安装更新失败", ex);
            StatusText.Text = "更新失败：" + ex.Message;
            SetBusy(false);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _cts?.Cancel();
        if (SkipBox.IsChecked == true && _canSelfUpdate && !_busy)
        {
            _settings.SkippedUpdateTag = _update.Tag;
            ConfigStore.Save(_settings);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = !busy;
        LaterButton.IsEnabled = !busy;
        SkipBox.IsEnabled = !busy;
        DownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy) DownloadProgress.Value = 0;
    }

    private static string FormatNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var text = notes.Replace("\r\n", "\n");
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1");
        text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline);
        text = text.Replace("**", "").Replace("__", "").Replace("`", "");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        if (bytes >= 1024L * 1024) return $"{bytes / 1024d / 1024d:F1} MB";
        return $"{bytes / 1024d:F0} KB";
    }
}
