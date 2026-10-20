using System.Diagnostics;
using cTools.Core.Index;
using cTools.Core.Launching;
using cTools.Core.Models;
using cTools.Core.Scanning;
using cTools.Core.Storage;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
switch (command)
{
    case "scan":
    {
        var settings = ConfigStore.Load();
        var scanner = new AppScanner();
        var progress = new Progress<string>(s => Console.WriteLine($"[scan] {s}"));
        var sw = Stopwatch.StartNew();
        var entries = await scanner.ScanAsync(settings, progress);
        sw.Stop();
        Console.WriteLine($"扫描完成: {entries.Count} 项，用时 {sw.ElapsedMilliseconds} ms");
        foreach (var group in entries.GroupBy(e => e.Source).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key,-12} {group.Count(),5}");
        IndexCache.Save(entries);
        Console.WriteLine("索引已写入: " + AppPaths.IndexFile);
        foreach (var entry in entries.OrderBy(e => e.Name).Take(20))
            Console.WriteLine($"  {entry.Name}  ->  {entry.ResolvedTarget ?? entry.Target}");
        break;
    }
    case "search":
    {
        var query = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "";
        var entries = IndexCache.Load() ?? new List<AppEntry>();
        if (entries.Count == 0)
        {
            Console.WriteLine("索引为空，请先执行 scan");
            break;
        }
        var index = SearchIndex.Build(entries);
        var sw = Stopwatch.StartNew();
        var hits = index.Search(query, 20);
        sw.Stop();
        Console.WriteLine($"查询 \"{query}\" 用时 {sw.Elapsed.TotalMilliseconds:F2} ms，命中 {hits.Count} 项");
        foreach (var hit in hits)
            Console.WriteLine($"  {hit.Score,6}  {hit.Entry.Name}  [{hit.MatchKind}]  {hit.Entry.ResolvedTarget ?? hit.Entry.Target}");
        break;
    }
    case "bench":
    {
        var entries = IndexCache.Load() ?? new List<AppEntry>();
        var syntheticCount = args.Length > 1 && int.TryParse(args[1], out var requestedCount) ? requestedCount : 0;
        if (syntheticCount > 0)
        {
            Console.WriteLine($"使用 {syntheticCount} 条模拟数据");
            entries = BuildSyntheticEntries(syntheticCount);
        }
        else if (entries.Count == 0)
        {
            Console.WriteLine("索引为空，先生成 10000 条模拟数据");
            entries = BuildSyntheticEntries(10000);
        }
        var build = Stopwatch.StartNew();
        var index = SearchIndex.Build(entries);
        build.Stop();
        Console.WriteLine($"索引构建: {index.Count} 项，用时 {build.ElapsedMilliseconds} ms");

        var queries = new[] { "w", "we", "wei", "weixin", "chrome", "code", "vs", "设置", "微信", "te", "edge", "visual studio" };
        var samples = new List<double>();
        foreach (var query in queries)
        {
            for (var i = 0; i < 20; i++) index.Search(query, 20);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 100; i++) index.Search(query, 20);
            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds / 100;
            samples.Add(ms);
            Console.WriteLine($"  {query,-14} {ms:F3} ms/次");
        }
        samples.Sort();
        Console.WriteLine($"平均 {samples.Average():F3} ms，P95 {samples[(int)(samples.Count * 0.95)]:F3} ms");
        break;
    }
    case "sources":
    {
        Console.WriteLine("开始菜单:");
        foreach (var path in SystemPaths.StartMenuRoots()) Console.WriteLine("  " + path);
        Console.WriteLine("桌面:");
        foreach (var path in SystemPaths.DesktopRoots()) Console.WriteLine("  " + path);
        Console.WriteLine("App Paths: " + SystemPaths.AppPathsEntries().Count + " 项");
        break;
    }
    case "launch":
    {
        var target = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "";
        if (string.IsNullOrWhiteSpace(target)) break;
        var result = AppLauncher.Launch(new AppEntry { Name = "test", Target = target, Kind = EntryKind.App });
        Console.WriteLine(result.Success ? "启动成功" : "启动失败: " + result.Error);
        break;
    }
    default:
        Console.WriteLine("""
cTools CLI
  scan            扫描并缓存应用索引
  search <query>  在缓存索引中搜索
  bench           索引与搜索性能基准
  sources         查看默认扫描来源
  launch <path>   测试启动
""");
        break;
}

static List<AppEntry> BuildSyntheticEntries(int count)
{
    var list = new List<AppEntry>(count);
    var names = new[] { "微信", "企业微信", "Visual Studio Code", "Google Chrome", "Microsoft Edge", "Windows Terminal", "计算器", "记事本", "文件资源管理器", "设置" };
    for (var i = 0; i < count; i++)
    {
        var name = i < names.Length ? names[i] : $"App {i:00000}";
        list.Add(new AppEntry
        {
            Name = name,
            Target = $"C:\\Apps\\App{i}.exe",
            ResolvedTarget = $"C:\\Apps\\App{i}.exe",
            Kind = EntryKind.App,
            Source = EntrySource.StartMenu,
            UseCount = i % 17
        });
    }
    return list;
}
