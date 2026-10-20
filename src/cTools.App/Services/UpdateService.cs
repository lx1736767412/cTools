using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using cTools.Core.Logging;
using cTools.Core.Storage;
using cTools.Core.Updating;

namespace cTools.App.Services;

/// <summary>
/// 基于 GitHub Releases 的更新检查与安装。
/// Release 由 <c>.github/workflows/release.yml</c> 在每次 master 更新时创建。
/// </summary>
public sealed class UpdateService
{
    private const string Repository = "lx1736767412/cTools";
    private const string LatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    public static ReleaseVersion CurrentVersion { get; } = ReadCurrentVersion();

    public static string CurrentVersionText => CurrentVersion.IsValid ? CurrentVersion.Display : "未知版本";

    /// <summary>当前进程是否位于安装目录；便携版不执行自更新。</summary>
    public bool IsInstalled
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                var installRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "cTools");
                return !string.IsNullOrWhiteSpace(exe)
                       && exe.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>查询最新正式 Release；无更新、无发布或版本不可解析时返回 null。</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd($"cTools/{CurrentVersion.Semver} (+https://github.com/{Repository})");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub 接口访问受限，请稍后再试。");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True) return null;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
        var version = ReleaseVersion.Parse(tag);
        if (!version.IsValid) return null;

        var title = root.TryGetProperty("name", out var nameElement) && !string.IsNullOrWhiteSpace(nameElement.GetString())
            ? nameElement.GetString()!
            : $"cTools {version.Semver}";

        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";
        var releaseUrl = root.TryGetProperty("html_url", out var htmlElement)
            ? htmlElement.GetString() ?? $"https://github.com/{Repository}/releases"
            : $"https://github.com/{Repository}/releases";

        var publishedAt = root.TryGetProperty("published_at", out var publishedElement)
                          && DateTimeOffset.TryParse(publishedElement.GetString(), out var published)
            ? published
            : DateTimeOffset.MinValue;

        var asset = SelectInstaller(root);
        if (asset is null) return null;

        return new UpdateInfo(
            version,
            tag,
            title,
            notes,
            publishedAt,
            asset.Value.Url,
            asset.Value.Name,
            asset.Value.Size,
            asset.Value.Sha256,
            releaseUrl);
    }

    /// <summary>下载安装包到本地更新目录，返回安装包路径。可用 digest 时会校验 SHA-256。</summary>
    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDir);

        var fileName = SanitizeFileName(update.DownloadName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"cTools-{update.Version.Semver}-setup.exe";
        var target = Path.Combine(AppPaths.UpdatesDir, fileName);
        var temporary = target + ".download";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        request.Headers.UserAgent.ParseAdd($"cTools/{CurrentVersion.Semver} (+https://github.com/{Repository})");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? update.Size;
        using var hasher = string.IsNullOrWhiteSpace(update.Sha256)
            ? null
            : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var output = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                hasher?.AppendData(buffer, 0, read);
                received += read;
                if (total > 0) progress?.Report(Math.Clamp((double)received / total, 0, 1));
            }

            await output.FlushAsync(timeout.Token).ConfigureAwait(false);

            if (hasher is not null)
            {
                var actual = Convert.ToHexString(hasher.GetHashAndReset());
                if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新包校验失败，已放弃安装。");
            }

            if (received <= 0) throw new InvalidDataException("更新包下载为空。");
            if (update.Size > 0 && received < update.Size)
                throw new InvalidDataException("更新包大小不完整，已放弃安装。");
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        File.Move(temporary, target, overwrite: true);
        progress?.Report(1);
        return target;
    }

    /// <summary>启动安装程序；<c>/AUTOUPDATE=1</c> 让安装结束后自动重新打开 cTools。</summary>
    public void LaunchInstaller(string installerPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /AUTOUPDATE=1",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppPaths.UpdatesDir
        };
        Process.Start(startInfo);
    }

    /// <summary>只保留最近一个更新包，避免更新目录无限增长。</summary>
    public void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(AppPaths.UpdatesDir)) return;
            var files = new DirectoryInfo(AppPaths.UpdatesDir)
                .EnumerateFiles()
                .Where(file => file.Extension is ".exe" or ".download")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(1)
                .ToList();
            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // 正被其他进程占用时下次再清理。
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("清理旧更新包失败", ex);
        }
    }

    public static void OpenReleasePage(UpdateInfo update)
    {
        try
        {
            Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("打开更新页面失败", ex);
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static ReleaseVersion ReadCurrentVersion()
    {
        try
        {
            var assembly = typeof(UpdateService).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var parsed = ReleaseVersion.Parse(informational);
            if (parsed.IsValid) return parsed;

            var version = assembly.GetName().Version;
            if (version is null) return default;
            return new ReleaseVersion(version.Major, version.Minor, Math.Max(version.Build, 0), 0);
        }
        catch
        {
            return default;
        }
    }

    private static (string Name, string Url, long Size, string? Sha256)? SelectInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var candidates = new List<(string Name, string Url, long Size, string? Sha256)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var value) ? value : 0;
            string? sha256 = null;
            if (asset.TryGetProperty("digest", out var digestElement))
            {
                var digest = digestElement.GetString();
                if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha256 = digest["sha256:".Length..].Trim();
            }
            candidates.Add((name, url, size, sha256));
        }

        var setup = candidates
            .Where(candidate => candidate.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(setup.Name)) return setup;

        var fallback = candidates.FirstOrDefault();
        return string.IsNullOrWhiteSpace(fallback.Name) ? null : fallback;
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var fileName = Path.GetFileName(name);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return fileName;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略清理失败。
        }
    }
}
