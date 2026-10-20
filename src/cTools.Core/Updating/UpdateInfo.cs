using System.Globalization;
using System.Text.RegularExpressions;

namespace cTools.Core.Updating;

/// <summary>
/// 发布版本号。支持 <c>0.1.0</c>、<c>v0.1.0</c>、<c>0.1.0-build.12</c> 这类 GitHub Release 标签。
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, int Build) : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"^v?(\d+)\.(\d+)\.(\d+)(?:[-+.]?(?:build|b)\.?(\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ReleaseVersion Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var value = text.Trim();
        // InformationalVersion 可能带有 +<commit> 元数据。
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];

        var match = Pattern.Match(value);
        if (!match.Success) return default;

        return new ReleaseVersion(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0);
    }

    public bool IsValid => Major > 0 || Minor > 0 || Patch > 0;

    public string Semver => $"{Major}.{Minor}.{Patch}";

    public string Display => Build > 0 ? $"{Semver} · build {Build}" : Semver;

    public int CompareTo(ReleaseVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : Build.CompareTo(other.Build);
    }

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() => Display;
}

/// <summary>一个可下载的 GitHub Release 更新。</summary>
public sealed record UpdateInfo(
    ReleaseVersion Version,
    string Tag,
    string Title,
    string Notes,
    DateTimeOffset PublishedAt,
    string DownloadUrl,
    string DownloadName,
    long Size,
    string? Sha256,
    string ReleaseUrl);
