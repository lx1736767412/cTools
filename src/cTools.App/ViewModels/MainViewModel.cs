using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using cTools.Core.Index;
using cTools.Core.Launching;
using cTools.Core.Logging;
using cTools.Core.Models;
using cTools.Core.Scanning;
using cTools.Core.Storage;
using cTools.App.Services;

namespace cTools.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppScanner _scanner = new();
    private List<AppEntry> _entries = new();
    private SearchIndex _index = SearchIndex.Build(Array.Empty<AppEntry>());
    private CancellationTokenSource? _scanCts;
    private DispatcherTimer? _saveTimer;
    private string _query = "";
    private ResultItem? _selected;
    private string _status = "";
    private string _scanSummary = "尚未扫描";
    private bool _isIndexing;

    public MainViewModel()
        : this(null)
    {
    }

    public MainViewModel(AppSettings? settings)
    {
        Settings = settings?.Clone() ?? ConfigStore.Load();
    }

    public AppSettings Settings { get; private set; }
    public ObservableCollection<ResultItem> Results { get; } = new();
    public event Action? SettingsChanged;

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value)) return;
            Refresh();
        }
    }

    public ResultItem? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string ScanSummary
    {
        get => _scanSummary;
        private set => SetProperty(ref _scanSummary, value);
    }

    public bool IsIndexing
    {
        get => _isIndexing;
        private set => SetProperty(ref _isIndexing, value);
    }

    public bool HasResults => Results.Count > 0;
    public string CountText => Results.Count == 0 ? "" : $"{Results.Count} 个结果";

    public void Initialize()
    {
        var cached = IndexCache.Load();
        if (cached is { Count: > 0 })
        {
            _entries = cached;
            _index = SearchIndex.Build(_entries);
            Status = $"{_entries.Count} 项 · 缓存索引";
            UpdateScanSummary();
        }
        else
        {
            Status = "正在建立索引…";
        }
        Refresh();
        _ = RebuildIndexAsync();
    }

    public async Task RebuildIndexAsync(AppSettings? scanSettings = null, bool persistCache = true)
    {
        var settings = scanSettings ?? Settings;
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        IsIndexing = true;
        Status = "正在扫描应用…";
        try
        {
            var progress = new Progress<string>(source => Status = "正在扫描：" + source);
            var entries = await _scanner.ScanAsync(settings, progress, cts.Token);
            if (cts.IsCancellationRequested) return;
            MergeUsage(entries);
            _entries = entries;
            _index = SearchIndex.Build(entries);
            if (persistCache) IndexCache.Save(entries);
            Status = $"{entries.Count} 项 · 更新于 {DateTime.Now:HH:mm}";
            UpdateScanSummary();
            Refresh();
        }
        catch (OperationCanceledException)
        {
            // 新的扫描请求已经取代当前任务。
        }
        catch (Exception ex)
        {
            Log.Error("重建索引失败", ex);
            Status = "扫描失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts))
            {
                _scanCts = null;
                IsIndexing = false;
            }
            cts.Dispose();
        }
    }

    public void Refresh()
    {
        var selectedId = Selected?.Entry.Id;
        var hits = _index.Search(Query, Math.Clamp(Settings.MaxResults, 5, 200));
        Results.Clear();
        foreach (var hit in hits)
            Results.Add(new ResultItem(hit.Entry));
        if (selectedId is not null)
            Selected = Results.FirstOrDefault(r => r.Entry.Id == selectedId) ?? Results.FirstOrDefault();
        else
            Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CountText));
    }

    public bool LaunchSelected(bool admin, bool openLocation)
    {
        var item = Selected;
        if (item is null) return false;
        if (openLocation)
        {
            if (item.Entry.Kind is EntryKind.Url or EntryKind.Uwp or EntryKind.SystemSetting)
            {
                Status = "该启动项没有本地位置";
                return false;
            }
            var target = item.Entry.ResolvedTarget ?? item.Entry.Target;
            var locationResult = AppLauncher.OpenLocation(target);
            if (!locationResult.Success)
            {
                Status = locationResult.Error ?? "无法打开位置";
                return false;
            }
            return true;
        }

        var result = AppLauncher.Launch(item.Entry, admin);
        if (!result.Success)
        {
            Status = result.Error ?? "启动失败";
            return false;
        }
        item.Entry.UseCount++;
        item.Entry.LastUsedAt = DateTime.Now;
        ScheduleSave();
        if (string.IsNullOrEmpty(Query)) Refresh();
        return true;
    }

    public void TogglePin()
    {
        if (Selected is null) return;
        Selected.Entry.Pinned = !Selected.Entry.Pinned;
        ScheduleSave();
        Refresh();
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var raw in paths)
        {
            var path = raw.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (Directory.Exists(path))
            {
                if (Settings.CustomPaths.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                Settings.CustomPaths.Add(new CustomPath { Path = path, Recursive = true, Enabled = true });
                Settings.Search.CustomPaths = true;
                added++;
            }
            else if (File.Exists(path))
            {
                if (Settings.CustomEntries.Any(e => string.Equals(e.Target, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var name = EntryKindDetector.DefaultNameFor(path);
                Settings.CustomEntries.Add(new CustomEntry
                {
                    Name = string.IsNullOrWhiteSpace(name) ? path : name,
                    Target = path,
                    Kind = EntryKindDetector.Infer(path),
                    Enabled = true
                });
                Settings.Search.CustomEntries = true;
                added++;
            }
        }
        if (added == 0) return;
        ConfigStore.Save(Settings);
        Status = $"已添加 {added} 个自定义启动项";
        _ = RebuildIndexAsync();
    }

    public void RemoveSelectedCustom()
    {
        if (Selected is null) return;
        var entry = Selected.Entry;
        var removed = false;
        if (entry.Source == EntrySource.CustomEntry)
        {
            var before = Settings.CustomEntries.Count;
            Settings.CustomEntries.RemoveAll(e => string.Equals(e.Target, entry.Target, StringComparison.OrdinalIgnoreCase));
            removed = Settings.CustomEntries.Count != before;
        }
        else if (entry.Source == EntrySource.CustomPath)
        {
            var before = Settings.CustomPaths.Count;
            Settings.CustomPaths.RemoveAll(p => entry.Target.StartsWith(p.Path, StringComparison.OrdinalIgnoreCase));
            removed = Settings.CustomPaths.Count != before;
        }
        if (!removed) return;
        ConfigStore.Save(Settings);
        _ = RebuildIndexAsync();
    }

    public void ApplySettings(AppSettings settings)
    {
        Settings = settings.Clone();
        ConfigStore.Save(Settings);
        SettingsChanged?.Invoke();
        _ = RebuildIndexAsync();
    }

    public int CountBySource(EntrySource source) => _entries.Count(e => e.Source == source);

    private void UpdateScanSummary()
    {
        if (_entries.Count == 0)
        {
            ScanSummary = "尚未扫描到启动项";
            return;
        }
        var labels = new (EntrySource Source, string Label)[]
        {
            (EntrySource.StartMenu, "开始菜单"),
            (EntrySource.Desktop, "桌面"),
            (EntrySource.AppPaths, "App Paths"),
            (EntrySource.Uwp, "UWP"),
            (EntrySource.CustomEntry, "自定义"),
            (EntrySource.CustomPath, "扫描路径"),
            (EntrySource.SystemSetting, "系统设置")
        };
        var parts = labels
            .Select(item => new { item.Label, Count = CountBySource(item.Source) })
            .Where(item => item.Count > 0)
            .Select(item => $"{item.Label} {item.Count}");
        ScanSummary = $"共 {_entries.Count} 项 · " + string.Join(" · ", parts);
    }

    private void MergeUsage(List<AppEntry> entries)
    {
        if (_entries.Count == 0) return;
        var old = _entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!old.TryGetValue(entry.Id, out var previous)) continue;
            entry.UseCount = previous.UseCount;
            entry.LastUsedAt = previous.LastUsedAt;
            entry.Pinned = previous.Pinned;
        }
    }

    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                var snapshot = _entries;
                _ = Task.Run(() => IndexCache.Save(snapshot));
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
