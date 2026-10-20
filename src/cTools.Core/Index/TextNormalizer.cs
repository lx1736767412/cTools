namespace cTools.Core.Index;

public static class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var buffer = new char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            var x = c;
            if (x >= '\uFF01' && x <= '\uFF5E') x = (char)(x - 0xFEE0);
            else if (x == '\u3000') x = ' ';
            if (char.IsWhiteSpace(x))
            {
                if (length > 0 && buffer[length - 1] != ' ') buffer[length++] = ' ';
            }
            else
            {
                buffer[length++] = char.ToLowerInvariant(x);
            }
        }
        return new string(buffer, 0, length).Trim();
    }

    public static bool ContainsCjk(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var c in value)
            if (IsCjk(c)) return true;
        return false;
    }

    public static bool IsCjk(char c) => c >= '\u4E00' && c <= '\u9FFF';
}
