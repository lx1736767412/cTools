using System.Drawing;
using System.Windows.Forms;

namespace cTools.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon? _appIcon;

    public TrayService(Action show, Action settings, Action rebuild, Action checkUpdates, Action exit)
    {
        _menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("打开 cTools") { Font = new Font(_menu.Font, System.Drawing.FontStyle.Bold) };
        showItem.Click += (_, _) => show();
        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => settings();
        var rebuildItem = new ToolStripMenuItem("重建索引");
        rebuildItem.Click += (_, _) => rebuild();
        var checkUpdatesItem = new ToolStripMenuItem("检查更新");
        checkUpdatesItem.Click += (_, _) => checkUpdates();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => exit();
        _menu.Items.Add(showItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(rebuildItem);
        _menu.Items.Add(checkUpdatesItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _appIcon = LoadAppIcon();
        _icon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            Text = "cTools",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void SetVisible(bool visible) => _icon.Visible = visible;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon?.Dispose();
        _menu.Dispose();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/cTools;component/Assets/cTools.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var icon = new Icon(stream);
                return (Icon)icon.Clone();
            }
        }
        catch
        {
            // 资源读取失败时退回 exe 图标。
        }

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
                return Icon.ExtractAssociatedIcon(executable);
        }
        catch
        {
            // 忽略，最后使用系统默认图标。
        }
        return null;
    }
}
