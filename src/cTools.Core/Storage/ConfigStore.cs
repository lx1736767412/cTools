using System.Text.Json;
using System.Text.Json.Serialization;
using cTools.Core.Logging;
using cTools.Core.Models;
using cTools.Core.Scanning;

namespace cTools.Core.Storage;

public static class AppPaths
{
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cTools");
    public static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cTools");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string IndexFile => Path.Combine(LocalRoot, "index.json");
    public static string IconCacheDir => Path.Combine(LocalRoot, "icons");
    public static string UpdatesDir => Path.Combine(LocalRoot, "updates");
    public static string LogFile => Path.Combine(LocalRoot, "logs", "ctools.log");

    /// <summary>从改名前的 RTools 目录一次性迁移设置和索引，避免重命名后丢失配置。</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            var legacyDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RTools");
            var legacyLocalRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RTools");
            MigrateFile(Path.Combine(legacyDataRoot, "settings.json"), SettingsFile, DataRoot);
            MigrateFile(Path.Combine(legacyLocalRoot, "index.json"), IndexFile, LocalRoot);
        }
        catch (Exception ex)
        {
            Log.Error("迁移旧版 RTools 数据失败", ex);
        }
    }

    private static void MigrateFile(string source, string destination, string destinationDirectory)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(destinationDirectory);
        File.Copy(source, destination);
    }
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        AppPaths.MigrateLegacyData();
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null) return new AppSettings();
            settings.Search ??= new SearchScopeSettings();
            settings.CustomPaths ??= new List<CustomPath>();
            settings.CustomEntries ??= new List<CustomEntry>();
            settings.ScanExtensions ??= new List<string>();
            settings.ExcludePatterns ??= new List<string>();
            if (settings.Version < 2) MigrateToVersion2(settings);
            TextEditorCatalog.EnsureConfigured(settings);
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("加载设置失败，使用默认设置", ex);
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                    File.Move(AppPaths.SettingsFile, AppPaths.SettingsFile + ".broken", true);
            }
            catch { }
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.SettingsFile, true);
        }
        catch (Exception ex)
        {
            Log.Error("保存设置失败", ex);
        }
    }

    private static void MigrateToVersion2(AppSettings settings)
    {
        // v1 默认会收录 .url / ClickOnce / 批处理 / PowerShell / msi；这些来源噪音大，v2 起默认关闭。
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".url", ".appref-ms", ".bat", ".cmd", ".ps1", ".msi"
        };
        settings.ScanExtensions = (settings.ScanExtensions ?? new List<string>())
            .Where(extension => !removed.Contains(EntryKindDetector.NormalizeExtension(extension)))
            .ToList();
        if (settings.ScanExtensions.Count == 0)
            settings.ScanExtensions = new List<string> { ".exe", ".lnk" };
        settings.Version = 2;
        Log.Info("设置已迁移到 v2：默认只扫描 .exe / .lnk");
        Save(settings);
    }
}
