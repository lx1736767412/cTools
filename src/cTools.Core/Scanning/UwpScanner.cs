using cTools.Core.Models;

namespace cTools.Core.Scanning;

/// <summary>
/// 兼容旧调用：UWP / MSIX 应用已经并入 <see cref="AppsFolderScanner"/> 的统一枚举。
/// </summary>
public static class UwpScanner
{
    public static IEnumerable<AppEntry> Scan() => AppsFolderScanner.Scan();
}
