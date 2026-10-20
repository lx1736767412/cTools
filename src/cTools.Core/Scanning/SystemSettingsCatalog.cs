using cTools.Core.Models;

namespace cTools.Core.Scanning;

public static class SystemSettingsCatalog
{
    private static readonly (string Name, string Uri, string Keywords)[] Items =
    {
        ("显示设置", "ms-settings:display", "display screen monitor 显示 屏幕"),
        ("声音设置", "ms-settings:sound", "sound audio speaker 声音 音量"),
        ("通知设置", "ms-settings:notifications", "notification 通知"),
        ("电源和电池", "ms-settings:powersleep", "power battery sleep 电源 电池"),
        ("存储设置", "ms-settings:storagesense", "storage disk 存储 磁盘"),
        ("应用和功能", "ms-settings:appsfeatures", "apps programs 应用 程序 卸载"),
        ("默认应用", "ms-settings:defaultapps", "default apps 默认应用"),
        ("蓝牙和其他设备", "ms-settings:bluetooth", "bluetooth devices 蓝牙 设备"),
        ("网络和 Internet", "ms-settings:network", "network wifi internet 网络"),
        ("代理设置", "ms-settings:network-proxy", "proxy 代理"),
        ("日期和时间", "ms-settings:dateandtime", "date time 日期 时间"),
        ("语言和区域", "ms-settings:regionlanguage", "language region 语言 区域"),
        ("隐私和安全性", "ms-settings:privacy", "privacy security 隐私 安全"),
        ("Windows 更新", "ms-settings:windowsupdate", "windows update 更新"),
        ("开发者设置", "ms-settings:developers", "developer 开发者"),
        ("鼠标设置", "ms-settings:mousetouchpad", "mouse touchpad 鼠标"),
        ("键盘设置", "ms-settings:keyboard", "keyboard 键盘"),
        ("辅助功能", "ms-settings:easeofaccess", "accessibility 辅助功能"),
        ("关于本机", "ms-settings:about", "about system 关于 系统"),
        ("任务栏设置", "ms-settings:taskbar", "taskbar 任务栏")
    };

    public static IEnumerable<AppEntry> GetEntries()
    {
        foreach (var (name, uri, keywords) in Items)
        {
            yield return new AppEntry
            {
                Name = name,
                Target = uri,
                ResolvedTarget = uri,
                Kind = EntryKind.SystemSetting,
                Source = EntrySource.SystemSetting,
                Description = "Windows 设置",
                Keywords = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
            };
        }
    }
}
