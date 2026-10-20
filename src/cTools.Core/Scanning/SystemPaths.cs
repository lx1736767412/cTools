using Microsoft.Win32;

namespace cTools.Core.Scanning;

public static class SystemPaths
{
    public static IReadOnlyList<string> StartMenuRoots()
    {
        var roots = new List<string>();
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> DesktopRoots()
    {
        var roots = new List<string>();
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyDictionary<string, string> AppPathsEntries()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", result);
        ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", result);
        ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths", result);
        return result;
    }

    private static void ReadHive(RegistryKey root, string path, IDictionary<string, string> result)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetSubKeyNames())
            {
                try
                {
                    using var appKey = key.OpenSubKey(name);
                    var value = appKey?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                    if (!File.Exists(expanded)) continue;
                    result.TryAdd(name, expanded);
                }
                catch
                {
                    // 单个 App Paths 项损坏不影响其他项。
                }
            }
        }
        catch
        {
            // 注册表不可读时跳过该来源。
        }
    }

    private static void Add(ICollection<string> roots, string root, string? child = null)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var path = child is null ? root : Path.Combine(root, child);
        if (Directory.Exists(path)) roots.Add(path);
    }
}
