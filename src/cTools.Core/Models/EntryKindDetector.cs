namespace cTools.Core.Models;

/// <summary>根据路径 / URL 推断启动项类型，供设置页和拖放共用。</summary>
public static class EntryKindDetector
{
    private static readonly HashSet<string> CommandExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".sh"
    };

    private static readonly HashSet<string> AppExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".msi", ".appx", ".appxbundle", ".msix"
    };

    public static EntryKind Infer(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return EntryKind.App;
        var value = target.Trim().Trim('"');
        if (Directory.Exists(value)) return EntryKind.Folder;
        if (IsWebUrl(value)) return EntryKind.Url;

        var extension = Path.GetExtension(value);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
            return EntryKind.Shortcut;
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            return EntryKind.Url;
        if (CommandExtensions.Contains(extension)) return EntryKind.Command;
        if (AppExtensions.Contains(extension)) return EntryKind.App;
        if (extension.Length == 0) return EntryKind.Command;
        return EntryKind.File;
    }

    public static bool IsWebUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static string NormalizeExtension(string? value)
    {
        var extension = (value ?? "").Trim();
        if (extension.Length == 0) return "";
        if (!extension.StartsWith('.')) extension = "." + extension;
        return extension.ToLowerInvariant();
    }

    public static string DefaultNameFor(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "";
        var value = target.Trim().Trim('"');
        if (IsWebUrl(value))
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase)
                : value;
        }
        if (Directory.Exists(value))
        {
            var trimmed = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed);
        }
        var name = Path.GetFileNameWithoutExtension(value);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(value) : name;
    }
}
