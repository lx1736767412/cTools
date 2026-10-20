using cTools.Core.Models;

namespace cTools.Core.Index;

public sealed class SearchHit
{
    public required AppEntry Entry { get; init; }
    public int Score { get; init; }
    public string MatchKind { get; init; } = "";
}

public sealed class SearchIndex
{
    private sealed class Indexed
    {
        public required AppEntry Entry;
        public string Name = "";
        public string Pinyin = "";
        public string Initials = "";
        public string Acronym = "";
        public string[] Tokens = Array.Empty<string>();
        public string[] Keywords = Array.Empty<string>();
        public string Searchable = "";
        public int SourceWeight;
    }

    private readonly Indexed[] _entries;
    public DateTime BuiltAt { get; }
    public int Count => _entries.Length;

    private SearchIndex(Indexed[] entries, DateTime builtAt)
    {
        _entries = entries;
        BuiltAt = builtAt;
    }

    public static SearchIndex Build(IEnumerable<AppEntry> source)
    {
        var list = new List<Indexed>(source is ICollection<AppEntry> collection ? collection.Count : 512);
        foreach (var entry in source)
        {
            if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Target))
                continue;
            entry.EnsureId();
            var name = TextNormalizer.Normalize(entry.Name);
            var keywords = (entry.Keywords ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(TextNormalizer.Normalize)
                .Distinct()
                .ToArray();
            list.Add(new Indexed
            {
                Entry = entry,
                Name = name,
                Pinyin = PinyinDictionary.GetPinyin(entry.Name),
                Initials = PinyinDictionary.GetInitials(entry.Name),
                Acronym = PinyinDictionary.GetAcronym(entry.Name),
                Tokens = PinyinDictionary.GetTokens(entry.Name).ToArray(),
                Keywords = keywords,
                Searchable = TextNormalizer.Normalize($"{entry.Name} {entry.Description} {entry.ResolvedTarget} {entry.Target} {string.Join(' ', entry.Keywords ?? new List<string>())}"),
                SourceWeight = Weight(entry.Source)
            });
        }
        list.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return new SearchIndex(list.ToArray(), DateTime.UtcNow);
    }

    public IReadOnlyList<SearchHit> Search(string? query, int limit)
    {
        var normalized = TextNormalizer.Normalize(query);
        if (normalized.Length == 0) return Home(limit);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<SearchHit>(Math.Min(limit, 64));
        foreach (var entry in _entries)
        {
            var total = 0;
            var kind = "";
            var matchedAll = true;
            foreach (var token in tokens)
            {
                var score = ScoreToken(entry, token, out var tokenKind);
                if (score <= 0)
                {
                    matchedAll = false;
                    break;
                }
                total += score;
                if (kind.Length == 0) kind = tokenKind;
            }
            if (!matchedAll) continue;
            total += (int)(Math.Log2(entry.Entry.UseCount + 1) * 18);
            if (entry.Entry.Pinned) total += 2000;
            if (entry.Entry.LastUsedAt is { } last)
            {
                var days = (DateTime.UtcNow - last).TotalDays;
                if (days <= 7) total += 120;
                else if (days <= 30) total += 60;
            }
            total += entry.SourceWeight;
            hits.Add(new SearchHit { Entry = entry.Entry, Score = total, MatchKind = kind });
        }
        hits.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0) return byScore;
            var byLength = a.Entry.Name.Length.CompareTo(b.Entry.Name.Length);
            return byLength != 0 ? byLength : string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
        return hits;
    }

    public IReadOnlyList<SearchHit> Home(int limit)
    {
        var hits = new List<SearchHit>(Math.Min(limit, 64));
        foreach (var entry in _entries)
        {
            // 空查询只展示“用过的”应用（以及手动固定的项），全新安装时保持空白。
            if (entry.Entry.UseCount <= 0 && entry.Entry.LastUsedAt is null && !entry.Entry.Pinned) continue;
            var score = (int)(Math.Log2(entry.Entry.UseCount + 1) * 20) + entry.SourceWeight;
            if (entry.Entry.Pinned) score += 10000;
            if (entry.Entry.LastUsedAt is { } last)
            {
                var days = (DateTime.UtcNow - last).TotalDays;
                if (days <= 7) score += 200;
                else if (days <= 30) score += 80;
            }
            hits.Add(new SearchHit { Entry = entry.Entry, Score = score, MatchKind = "home" });
        }
        hits.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0) return byScore;
            return string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
        return hits;
    }

    private static int ScoreToken(Indexed entry, string token, out string kind)
    {
        kind = "";
        if (token.Length == 0) return 0;
        var best = 0;
        var bestKind = "";

        if (entry.Name.Equals(token, StringComparison.Ordinal))
        {
            kind = "exact";
            return 1000;
        }
        if (entry.Name.StartsWith(token, StringComparison.Ordinal)) best = Set(best, 900, "prefix");
        else if (token.Length >= 2 && entry.Name.Contains(token, StringComparison.Ordinal)) best = Set(best, 600, "contains");

        foreach (var word in entry.Tokens)
        {
            if (word.Equals(token, StringComparison.Ordinal)) { best = Set(best, 850, "word"); break; }
            if (word.StartsWith(token, StringComparison.Ordinal)) best = Set(best, 800, "word-prefix");
        }

        if (entry.Pinyin.Equals(token, StringComparison.Ordinal)) best = Set(best, 950, "pinyin");
        else if (entry.Pinyin.StartsWith(token, StringComparison.Ordinal)) best = Set(best, 800, "pinyin-prefix");
        else if (token.Length >= 2 && entry.Pinyin.Contains(token, StringComparison.Ordinal)) best = Set(best, 450, "pinyin-contains");

        if (entry.Initials.Equals(token, StringComparison.Ordinal)) best = Set(best, 900, "initials");
        else if (entry.Initials.StartsWith(token, StringComparison.Ordinal)) best = Set(best, 750, "initials-prefix");
        else if (token.Length >= 2 && entry.Initials.Contains(token, StringComparison.Ordinal)) best = Set(best, 350, "initials-contains");

        if (entry.Acronym.Equals(token, StringComparison.Ordinal)) best = Set(best, 850, "acronym");
        else if (entry.Acronym.StartsWith(token, StringComparison.Ordinal)) best = Set(best, 650, "acronym-prefix");

        foreach (var keyword in entry.Keywords)
        {
            if (keyword.Equals(token, StringComparison.Ordinal)) { best = Set(best, 700, "keyword"); break; }
            if (keyword.StartsWith(token, StringComparison.Ordinal)) best = Set(best, 600, "keyword-prefix");
            else if (token.Length >= 2 && keyword.Contains(token, StringComparison.Ordinal)) best = Set(best, 300, "keyword-contains");
        }

        if (token.Length >= 2 && entry.Searchable.Contains(token, StringComparison.Ordinal))
            best = Set(best, 120, "path");

        if (token.Length == 1 && best < 800) return 0;
        kind = bestKind;
        return best;

        int Set(int current, int score, string name)
        {
            if (score <= current) return current;
            bestKind = name;
            return score;
        }
    }

    private static int Weight(EntrySource source) => source switch
    {
        EntrySource.Manual => 60,
        EntrySource.CustomEntry => 50,
        EntrySource.CustomPath => 40,
        EntrySource.AppsFolder => 28,
        EntrySource.AppPaths => 30,
        EntrySource.StartMenu => 20,
        EntrySource.Desktop => 15,
        EntrySource.Uwp => 10,
        EntrySource.SystemSetting => 5,
        _ => 0
    };
}
