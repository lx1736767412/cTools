using System.Text.Json.Serialization;

namespace cTools.Core.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum EscapeBehavior
{
    ClearThenHide,
    AlwaysHide
}

/// <summary>呼出面板在屏幕竖直方向上的锚点。</summary>
public enum PalettePosition
{
    Top,
    UpperCenter,
    Center,
    LowerCenter,
    Bottom
}

/// <summary>呼出面板使用哪一块显示器。</summary>
public enum MonitorPreference
{
    Cursor,
    Primary,
    FocusedWindow,
    Fixed
}

/// <summary>搜索结果的呈现方式。</summary>
public enum ResultLayout
{
    List,
    Grid
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string Hotkey { get; set; } = "Alt+Space";
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool CompactMode { get; set; }
    public bool StartWithWindows { get; set; }
    public bool HideOnBlur { get; set; } = true;
    public bool SelectSearchTextOnOpen { get; set; } = true;
    public bool KeepSearchText { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public int MaxResults { get; set; } = 50;

    // ── 更新 ─────────────────────────────────────────────────
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>用户选择“跳过此版本”时记录的 Release 标签。</summary>
    public string? SkippedUpdateTag { get; set; }

    // ── 呼出位置 ─────────────────────────────────────────────
    public MonitorPreference PreferredMonitor { get; set; } = MonitorPreference.Cursor;
    public string? PreferredMonitorDevice { get; set; }
    public PalettePosition PalettePosition { get; set; } = PalettePosition.UpperCenter;

    // ── 交互 ─────────────────────────────────────────────────
    public ResultLayout ResultLayout { get; set; } = ResultLayout.List;
    public EscapeBehavior EscapeBehavior { get; set; } = EscapeBehavior.ClearThenHide;
    public bool SpaceToLaunch { get; set; }
    public bool SuppressHotkeyInFullscreen { get; set; } = true;

    public SearchScopeSettings Search { get; set; } = new();
    public List<CustomPath> CustomPaths { get; set; } = new();
    public List<CustomEntry> CustomEntries { get; set; } = new();

    /// <summary>自定义扫描路径会收录的文件类型（含点号）。</summary>
    public List<string> ScanExtensions { get; set; } = new()
    {
        ".exe", ".lnk", ".url", ".appref-ms", ".bat", ".cmd", ".ps1", ".msi"
    };

    public List<string> ExcludePatterns { get; set; } = new()
    {
        "*uninstall*", "*卸载*", "*readme*", "*help*", "*帮助*", "*website*", "*网站*", "*文档*", "*manual*", "*license*", "*documentation*"
    };

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        var search = Search ?? new SearchScopeSettings();
        clone.Search = new SearchScopeSettings
        {
            StartMenu = search.StartMenu,
            Desktop = search.Desktop,
            AppPaths = search.AppPaths,
            Uwp = search.Uwp,
            SystemSettings = search.SystemSettings,
            CustomPaths = search.CustomPaths,
            CustomEntries = search.CustomEntries,
            IncludeSubfolders = search.IncludeSubfolders,
            MaxScanDepth = search.MaxScanDepth
        };
        clone.CustomPaths = (CustomPaths ?? new List<CustomPath>()).Select(p => p.Clone()).ToList();
        clone.CustomEntries = (CustomEntries ?? new List<CustomEntry>()).Select(e => e.Clone()).ToList();
        clone.ScanExtensions = new List<string>(ScanExtensions ?? new List<string>());
        clone.ExcludePatterns = new List<string>(ExcludePatterns ?? new List<string>());
        return clone;
    }
}

public sealed class SearchScopeSettings
{
    public bool StartMenu { get; set; } = true;
    public bool Desktop { get; set; } = true;
    public bool AppPaths { get; set; } = true;
    public bool Uwp { get; set; } = true;
    public bool SystemSettings { get; set; }
    public bool CustomPaths { get; set; } = true;
    public bool CustomEntries { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;
    public int MaxScanDepth { get; set; } = 6;
}

public sealed class CustomPath
{
    public string Path { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string KindLabel => Directory.Exists(Path) ? "文件夹" : File.Exists(Path) ? "文件" : "未找到";

    public CustomPath Clone() => new() { Path = Path, Recursive = Recursive, Enabled = Enabled };
}

public sealed class CustomEntry
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? IconPath { get; set; }
    public EntryKind Kind { get; set; } = EntryKind.App;
    public List<string> Keywords { get; set; } = new();
    public bool RunAsAdmin { get; set; }
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        EntryKind.App => "应用",
        EntryKind.Shortcut => "快捷方式",
        EntryKind.File => "文件",
        EntryKind.Folder => "文件夹",
        EntryKind.Url => "网址",
        EntryKind.Command => "命令",
        EntryKind.Uwp => "UWP",
        EntryKind.SystemSetting => "系统设置",
        _ => "启动项"
    };

    public CustomEntry Clone()
    {
        var clone = (CustomEntry)MemberwiseClone();
        clone.Keywords = new List<string>(Keywords ?? new List<string>());
        return clone;
    }
}
