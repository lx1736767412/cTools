using System.Text.Json;
using cTools.Core.Logging;
using cTools.Core.Models;

namespace cTools.Core.Storage;

public sealed class IndexCacheFile
{
    public int Version { get; set; } = 1;
    public DateTime GeneratedAt { get; set; }
    public List<AppEntry> Entries { get; set; } = new();
}

public static class IndexCache
{
    public static List<AppEntry>? Load()
    {
        AppPaths.MigrateLegacyData();
        try
        {
            if (!File.Exists(AppPaths.IndexFile)) return null;
            var json = File.ReadAllText(AppPaths.IndexFile);
            var file = JsonSerializer.Deserialize<IndexCacheFile>(json, ConfigStore.JsonOptions);
            if (file is null || file.Version != 1) return null;
            return file.Entries ?? new List<AppEntry>();
        }
        catch (Exception ex)
        {
            Log.Error("加载索引缓存失败", ex);
            return null;
        }
    }

    public static void Save(IReadOnlyList<AppEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalRoot);
            var file = new IndexCacheFile
            {
                Version = 1,
                GeneratedAt = DateTime.UtcNow,
                Entries = entries.ToList()
            };
            var json = JsonSerializer.Serialize(file, ConfigStore.JsonOptions);
            var temp = AppPaths.IndexFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.IndexFile, true);
        }
        catch (Exception ex)
        {
            Log.Error("保存索引缓存失败", ex);
        }
    }
}
