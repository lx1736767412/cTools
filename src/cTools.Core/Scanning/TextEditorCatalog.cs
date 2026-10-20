using System.Diagnostics;
using cTools.Core.Models;

namespace cTools.Core.Scanning;

/// <summary>文本编辑器发现与配置。VS Code / Cursor 等支持直接打开文件夹。</summary>
public static class TextEditorCatalog
{
    public static List<string> Discover()
    {
        var editors = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var full = Path.GetFullPath(path);
            if (!editors.Contains(full, StringComparer.OrdinalIgnoreCase)) editors.Add(full);
        }

        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft VS Code\Code.exe"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\cursor\Cursor.exe"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Cursor\Cursor.exe"));

        foreach (var (_, path) in SystemPaths.AppPathsEntries())
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Equals("Code.exe", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Cursor.exe", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Windsurf.exe", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase))
                Add(path);
        }

        return editors
            .OrderByDescending(SupportsFolders)
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>编辑器是否支持把文件夹作为工作区打开。</summary>
    public static bool SupportsFolders(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name is "code" or "code-insiders" or "cursor" or "windsurf" or "vscodium" or "codium";
    }

    public static string DisplayName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch
        {
            // 忽略，退回文件名。
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>清理已不存在的编辑器，补上自动发现的候选，并保证有一个可用选择。</summary>
    public static void EnsureConfigured(AppSettings settings)
    {
        var editors = (settings.TextEditors ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var discovered in Discover())
            if (!editors.Contains(discovered, StringComparer.OrdinalIgnoreCase))
                editors.Add(discovered);

        editors = editors
            .OrderByDescending(SupportsFolders)
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.TextEditors = editors;

        if (string.IsNullOrWhiteSpace(settings.TextEditorPath)
            || !editors.Contains(settings.TextEditorPath, StringComparer.OrdinalIgnoreCase))
            settings.TextEditorPath = editors.FirstOrDefault(SupportsFolders) ?? editors.FirstOrDefault();
    }
}
