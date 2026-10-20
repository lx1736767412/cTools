using System.Drawing;
using System.Windows.Forms;

namespace cTools.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayService(Action show, Action settings, Action rebuild, Action exit)
    {
        _menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("打开 cTools") { Font = new Font(_menu.Font, System.Drawing.FontStyle.Bold) };
        showItem.Click += (_, _) => show();
        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => settings();
        var rebuildItem = new ToolStripMenuItem("重建索引");
        rebuildItem.Click += (_, _) => rebuild();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => exit();
        _menu.Items.Add(showItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(rebuildItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
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
        _menu.Dispose();
    }
}
