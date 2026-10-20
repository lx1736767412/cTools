using System.Text;
using System.Text.Json;

namespace cTools.Core.Index;

public static class PinyinDictionary
{
    private sealed record Info(string Full, string Initial);
    private static readonly Lazy<Dictionary<char, Info>> Map = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dictionary<char, Info> Load()
    {
        var result = new Dictionary<char, Info>(22000);
        try
        {
            var assembly = typeof(PinyinDictionary).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("pinyin.json", StringComparison.OrdinalIgnoreCase));
            if (name is null) return result;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) return result;
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (raw is null) return result;
            foreach (var (key, value) in raw)
            {
                if (key.Length == 0) continue;
                var parts = value.Split('|');
                if (parts.Length < 2) continue;
                result[key[0]] = new Info(parts[0], parts[1]);
            }
        }
        catch
        {
            // 拼音数据损坏时降级为中文子串搜索，不影响启动。
        }
        return result;
    }

    public static string GetPinyin(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length * 2);
        foreach (var c in value)
        {
            if (Map.Value.TryGetValue(c, out var info)) sb.Append(info.Full);
            else if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string GetInitials(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        var separator = true;
        foreach (var c in value)
        {
            if (Map.Value.TryGetValue(c, out var info))
            {
                sb.Append(info.Initial);
                separator = false;
                continue;
            }
            if (char.IsLetterOrDigit(c))
            {
                if (separator || char.IsUpper(c)) sb.Append(char.ToLowerInvariant(c));
                separator = false;
            }
            else
            {
                separator = true;
            }
        }
        return sb.ToString();
    }

    public static string GetAcronym(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder();
        var wordStart = true;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (wordStart || char.IsUpper(c)) sb.Append(char.ToLowerInvariant(c));
                wordStart = false;
            }
            else
            {
                wordStart = true;
            }
        }
        return sb.ToString();
    }

    public static IReadOnlyList<string> GetTokens(string? value)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(value)) return tokens;
        var sb = new StringBuilder();
        var previousLower = false;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (char.IsUpper(c) && previousLower && sb.Length > 0) AddToken(tokens, sb);
                sb.Append(char.ToLowerInvariant(c));
                previousLower = char.IsLower(c);
            }
            else
            {
                AddToken(tokens, sb);
                previousLower = false;
            }
        }
        AddToken(tokens, sb);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder sb)
    {
        if (sb.Length == 0) return;
        var token = sb.ToString();
        sb.Clear();
        if (token.Length > 0 && !tokens.Contains(token)) tokens.Add(token);
    }
}
