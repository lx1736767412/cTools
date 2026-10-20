using System.Security.Cryptography;
using System.Text;

namespace cTools.Core.Models;

public enum EntryKind
{
    App,
    Shortcut,
    File,
    Folder,
    Url,
    Command,
    Uwp,
    SystemSetting
}

public enum EntrySource
{
    Manual,
    CustomEntry,
    CustomPath,
    AppPaths,
    StartMenu,
    Desktop,
    Uwp,
    SystemSetting
}

public sealed class AppEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string? ResolvedTarget { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? IconPath { get; set; }
    public string? AppUserModelId { get; set; }
    public string? Description { get; set; }
    public EntryKind Kind { get; set; } = EntryKind.App;
    public EntrySource Source { get; set; } = EntrySource.Manual;
    public List<string> Keywords { get; set; } = new();
    public bool RunAsAdmin { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Pinned { get; set; }
    public int UseCount { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public static string MakeId(EntryKind kind, string target, string name)
    {
        var raw = $"{kind}|{target}|{name}".ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    public void EnsureId()
    {
        if (string.IsNullOrEmpty(Id))
            Id = MakeId(Kind, string.IsNullOrWhiteSpace(ResolvedTarget) ? Target : ResolvedTarget!, Name);
    }

    public AppEntry Clone()
    {
        var clone = (AppEntry)MemberwiseClone();
        clone.Keywords = new List<string>(Keywords);
        return clone;
    }
}
