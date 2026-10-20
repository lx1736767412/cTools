using System.Diagnostics;
using cTools.Core.Index;
using cTools.Core.Models;

namespace cTools.Core.Scanning;

public sealed class AppScanner
{
    private static readonly HashSet<string> ShortcutExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".appref-ms"
    };

    private static readonly HashSet<string> DefaultExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".appref-ms", ".exe", ".bat", ".cmd", ".ps1", ".msi"
    };

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "sdk", "docs", "doc", "samples", "sample", "examples", "example", "demos", "demo", "documentation", "help"
    };

    public Task<List<AppEntry>> ScanAsync(AppSettings settings, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ScanCore(settings, progress, cancellationToken), cancellationToken);
    }

    private static List<AppEntry> ScanCore(AppSettings settings, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        return RunSta(() =>
        {
            var results = new List<AppEntry>(2048);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            var byTarget = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            var extensions = BuildExtensionSet(settings);
            settings.Search ??= new SearchScopeSettings();

            void Add(AppEntry entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Target))
                    return;
                if (entry.Source is not (EntrySource.CustomEntry or EntrySource.CustomPath)
                    && IsExcludedName(entry.Name, settings))
                    return;
                entry.EnsureId();
                var nameKey = TextNormalizer.Normalize(entry.Name);
                var targetKey = BuildTargetKey(entry);

                if (byName.TryGetValue(nameKey, out var existing))
                {
                    // 同名只保留一条；Win32/快捷方式优先于 UWP，先加入的自定义项优先于系统来源。
                    if (existing.Kind == EntryKind.Uwp && entry.Kind != EntryKind.Uwp)
                    {
                        results.Remove(existing);
                        seen.Remove(BuildKey(existing));
                        byTarget.Remove(BuildTargetKey(existing));
                        byName.Remove(nameKey);
                    }
                    else
                    {
                        return;
                    }
                }

                // 系统来源里同一个可执行文件可能以不同显示名出现（Chrome / Google Chrome），按目标去重。
                if (entry.Source is not (EntrySource.CustomEntry or EntrySource.CustomPath)
                    && byTarget.ContainsKey(targetKey))
                    return;

                var key = BuildKey(entry);
                if (!seen.Add(key)) return;
                results.Add(entry);
                byName[nameKey] = entry;
                if (entry.Source is not (EntrySource.CustomEntry or EntrySource.CustomPath))
                    byTarget[targetKey] = entry;
            }

            if (settings.Search.CustomEntries)
            {
                progress?.Report("自定义启动项");
                foreach (var custom in settings.CustomEntries ?? new List<CustomEntry>())
                {
                    if (!custom.Enabled || string.IsNullOrWhiteSpace(custom.Name) || string.IsNullOrWhiteSpace(custom.Target))
                        continue;
                    Add(new AppEntry
                    {
                        Name = custom.Name.Trim(),
                        Target = custom.Target.Trim(),
                        ResolvedTarget = custom.Target.Trim(),
                        Arguments = custom.Arguments,
                        WorkingDirectory = custom.WorkingDirectory,
                        IconPath = custom.IconPath,
                        Kind = custom.Kind,
                        Source = EntrySource.CustomEntry,
                        Keywords = new List<string>(custom.Keywords ?? new List<string>()),
                        RunAsAdmin = custom.RunAsAdmin,
                        Description = custom.Target
                    });
                }
            }

            if (settings.Search.CustomPaths)
            {
                progress?.Report("自定义路径");
                foreach (var customPath in (settings.CustomPaths ?? new List<CustomPath>()).Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Path)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddPath(customPath, settings, extensions, Add);
                }
            }

            if (settings.Search.AppPaths)
            {
                progress?.Report("App Paths");
                foreach (var (name, path) in SystemPaths.AppPathsEntries())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var display = GetDisplayName(path, Path.GetFileNameWithoutExtension(name));
                    if (IsExcludedName(display, settings) || IsExcludedFile(path, settings)) continue;
                    Add(new AppEntry
                    {
                        Name = display,
                        Target = path,
                        ResolvedTarget = path,
                        IconPath = path,
                        Kind = EntryKind.App,
                        Source = EntrySource.AppPaths,
                        Keywords = new List<string> { Path.GetFileNameWithoutExtension(name) },
                        Description = path
                    });
                }
            }

            if (settings.Search.StartMenu)
            {
                progress?.Report("开始菜单");
                foreach (var root in SystemPaths.StartMenuRoots())
                    ScanShortcutDirectory(root, EntrySource.StartMenu, settings, extensions, Add, cancellationToken, recursive: true);
            }

            if (settings.Search.Desktop)
            {
                progress?.Report("桌面");
                foreach (var root in SystemPaths.DesktopRoots())
                    ScanShortcutDirectory(root, EntrySource.Desktop, settings, extensions, Add, cancellationToken, recursive: settings.Search.IncludeSubfolders);
            }

            if (settings.Search.Uwp)
            {
                progress?.Report("UWP 应用");
                foreach (var entry in UwpScanner.Scan())
                    Add(entry);
            }

            if (settings.Search.SystemSettings)
            {
                progress?.Report("Windows 设置");
                foreach (var entry in SystemSettingsCatalog.GetEntries())
                    Add(entry);
            }

            results.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return results;
        });
    }

    private static HashSet<string> BuildExtensionSet(AppSettings settings)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in settings.ScanExtensions ?? new List<string>())
        {
            var extension = EntryKindDetector.NormalizeExtension(raw);
            if (extension.Length > 1) set.Add(extension);
        }
        // 配置缺失或用户清空时退回默认集合，避免整个索引静默变空。
        if (set.Count == 0) set.UnionWith(DefaultExtensions);
        return set;
    }

    private static void ScanShortcutDirectory(
        string root,
        EntrySource source,
        AppSettings settings,
        HashSet<string> extensions,
        Action<AppEntry> add,
        CancellationToken cancellationToken,
        bool recursive)
    {
        if (!Directory.Exists(root)) return;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive && settings.Search.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MaxRecursionDepth = Math.Max(1, settings.Search.MaxScanDepth)
        };
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file);
                if (!ShortcutExtensions.Contains(extension)) continue;
                if (extensions.Count > 0 && !extensions.Contains(extension)) continue;
                if (IsExcluded(file, settings)) continue;
                if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && IsWebUrl(file)) continue;
                var entry = CreateShortcutEntry(file, source);
                if (entry is not null) add(entry);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logging.Log.Error($"扫描目录失败: {root}", ex);
        }
    }

    private static void AddPath(CustomPath customPath, AppSettings settings, HashSet<string> extensions, Action<AppEntry> add)
    {
        var path = Environment.ExpandEnvironmentVariables(customPath.Path.Trim());
        try
        {
            if (File.Exists(path))
            {
                // 用户显式添加的单个文件不受扩展名白名单限制。
                var entry = CreateFileEntry(path, EntrySource.CustomPath, extensions, enforceExtension: false);
                if (entry is not null) add(entry);
                return;
            }
            if (!Directory.Exists(path)) return;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = customPath.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                MaxRecursionDepth = Math.Max(1, settings.Search.MaxScanDepth)
            };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                var extension = Path.GetExtension(file);
                if (!extensions.Contains(extension)) continue;
                // 自定义路径由用户显式添加，只应用名称排除规则，不套用系统来源的目录黑名单。
                if (IsExcludedName(Path.GetFileNameWithoutExtension(file), settings)) continue;
                var entry = CreateFileEntry(file, EntrySource.CustomPath, extensions);
                if (entry is not null) add(entry);
            }
        }
        catch (Exception ex)
        {
            Logging.Log.Error($"扫描自定义路径失败: {path}", ex);
        }
    }

    private static AppEntry? CreateShortcutEntry(string file, EntrySource source)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var info = ShellLink.Resolve(file);
        var target = info?.TargetPath;
        var description = string.IsNullOrWhiteSpace(target) ? file : target;
        var keywords = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(target))
            keywords.Add(Path.GetFileNameWithoutExtension(target));
        return new AppEntry
        {
            Name = name,
            Target = file,
            ResolvedTarget = string.IsNullOrWhiteSpace(target) ? file : target,
            Arguments = info?.Arguments,
            WorkingDirectory = info?.WorkingDirectory,
            IconPath = string.IsNullOrWhiteSpace(info?.IconPath) ? file : info!.IconPath,
            Kind = EntryKind.Shortcut,
            Source = source,
            Keywords = keywords,
            Description = description
        };
    }

    private static AppEntry? CreateFileEntry(string file, EntrySource source, HashSet<string> extensions, bool enforceExtension = true)
    {
        var extension = Path.GetExtension(file);
        if (enforceExtension && !extensions.Contains(extension)) return null;

        var name = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? GetDisplayName(file, Path.GetFileNameWithoutExtension(file))
            : Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            && TryReadUrl(file) is { } url)
        {
            return new AppEntry
            {
                Name = name,
                Target = url,
                ResolvedTarget = url,
                IconPath = file,
                Kind = EntryKind.Url,
                Source = source,
                Keywords = new List<string> { name },
                Description = url
            };
        }

        if (ShortcutExtensions.Contains(extension))
            return CreateShortcutEntry(file, source);

        var keywords = new List<string> { Path.GetFileNameWithoutExtension(file) };
        var parent = Path.GetFileName(Path.GetDirectoryName(file));
        if (!string.IsNullOrWhiteSpace(parent)) keywords.Add(parent);

        return new AppEntry
        {
            Name = name,
            Target = file,
            ResolvedTarget = file,
            IconPath = file,
            Kind = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? EntryKind.App : EntryKind.File,
            Source = source,
            Keywords = keywords,
            Description = file
        };
    }

    private static bool IsExcluded(string file, AppSettings settings)
    {
        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
            if (SkipFolders.Contains(part)) return true;

        return IsExcludedName(Path.GetFileNameWithoutExtension(file), settings);
    }

    private static bool IsExcludedName(string name, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        foreach (var pattern in settings.ExcludePatterns ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var normalized = pattern.Trim().Trim('*');
            if (normalized.Length == 0) continue;
            if (name.Contains(normalized, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsExcludedFile(string path, AppSettings settings)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;
        var extension = Path.GetExtension(path);
        return extension.Equals(".chm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hlp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebUrl(string file)
        => TryReadUrl(file) is { } url
           && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string? TryReadUrl(string file)
    {
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    var url = line[4..].Trim();
                    return string.IsNullOrWhiteSpace(url) ? null : url;
                }
            }
        }
        catch
        {
            // 无法读取的 .url 交给后续处理。
        }
        return null;
    }

    private static string GetDisplayName(string path, string fallback)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch
        {
            // 读取版本信息失败时使用文件名。
        }
        return fallback;
    }

    private static string BuildTargetKey(AppEntry entry)
    {
        var target = string.IsNullOrWhiteSpace(entry.ResolvedTarget) ? entry.Target : entry.ResolvedTarget;
        if (entry.Kind == EntryKind.Uwp && !string.IsNullOrWhiteSpace(entry.AppUserModelId))
            target = "uwp:" + entry.AppUserModelId;
        var arguments = (entry.Arguments ?? "").Trim();
        return $"{(target ?? "").Trim()}|{arguments}".ToLowerInvariant();
    }

    private static string BuildKey(AppEntry entry)
        => $"{TextNormalizer.Normalize(entry.Name)}|{BuildTargetKey(entry)}";

    private static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
        return result;
    }
}
