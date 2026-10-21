using System.Windows;
using System.Windows.Media;
using cTools.Core.Models;
using cTools.App.Services;

namespace cTools.App.ViewModels;

public sealed class ResultItem : ObservableObject
{
    private ImageSource? _icon;

    public ResultItem(AppEntry entry, Action? action = null)
    {
        Entry = entry;
        Action = action;
        Glyph = entry.Kind switch
        {
            EntryKind.Folder => "\uE8B7",
            EntryKind.Url => "\uE71B",
            EntryKind.SystemSetting => "\uE713",
            EntryKind.Command => "\uE756",
            _ => "\uE71D"
        };
        Subtitle = BuildSubtitle(entry);

        var iconPath = entry.IconPath ?? entry.ResolvedTarget ?? entry.Target;
        var fallback = entry.ResolvedTarget;
        if (IconService.TryGetCached(iconPath, fallback, out var cached))
            _icon = cached;
        else
            _ = LoadIconAsync(iconPath, fallback);
    }

    public AppEntry Entry { get; }
    public Action? Action { get; }
    public string Name => Entry.Name;
    public string Subtitle { get; }
    public string Glyph { get; }
    public bool Pinned => Entry.Pinned;
    public string ToolTip => Entry.ResolvedTarget ?? Entry.Target;
    public string GridToolTip => string.IsNullOrWhiteSpace(Subtitle) ? Name : $"{Name}{Environment.NewLine}{Subtitle}";

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!SetProperty(ref _icon, value)) return;
            // 真实图标加载后隐藏占位字形，避免图标透明区域透出底下的字形。
            OnPropertyChanged(nameof(ShowGlyph));
        }
    }

    /// <summary>没有可用图标时才显示占位字形；有图标时完全隐藏，透明区域不会透出旧图标。</summary>
    public bool ShowGlyph => _icon is null;

    private static string BuildSubtitle(AppEntry entry)
    {
        var text = entry.Description;
        if (string.IsNullOrWhiteSpace(text))
            text = entry.ResolvedTarget ?? entry.Target;
        if (string.IsNullOrWhiteSpace(text)) return "";
        return text.Length <= 110 ? text : text[..107] + "…";
    }

    private async Task LoadIconAsync(string path, string? fallback)
    {
        var icon = await Task.Run(() => IconService.GetIcon(path, fallback));
        if (icon is null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        await dispatcher.InvokeAsync(() => Icon = icon);
    }
}
