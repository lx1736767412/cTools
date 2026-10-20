using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace cTools.App.Services;

public static class IconService
{
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [Flags]
    private enum SIIGBF
    {
        None = 0,
        BiggerSizeOk = 0x00000001,
        IconOnly = 0x00000004
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, SIIGBF flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        [In] ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object shellItem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int iconIndex, IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint iconCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 优先使用 Windows Shell 的图标（<see cref="IShellItemImageFactory"/>），
    /// 失败时退回 <c>SHGetFileInfo</c>，最后直接读取 exe 的图标资源。
    /// </summary>
    public static ImageSource? GetIcon(string? path, string? fallbackPath = null)
    {
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(fallbackPath)) return null;
        if (path is not null && path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return null;

        var isUwp = path is not null && path.Contains('!') && !path.Contains('\\') && !path.Contains('/');
        var cacheKey = $"{(isUwp ? "uwp:" : "")}{path}|{fallbackPath}";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        ImageSource? result = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            isUwp = isUwp || (!path.Contains('\\') && !path.Contains('/') && !Path.IsPathRooted(path));
            if (isUwp)
            {
                result = GetShellImage(path, 64);
            }
            else if (Path.IsPathRooted(path) || File.Exists(path) || Directory.Exists(path))
            {
                result = GetShellImage(path, 64);
                if (result is null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    result = GetExeIcon(path);
                result ??= GetFileIcon(path);
            }
        }

        if (result is null && !string.IsNullOrWhiteSpace(fallbackPath) && File.Exists(fallbackPath))
        {
            result = GetShellImage(fallbackPath, 64)
                     ?? (fallbackPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? GetExeIcon(fallbackPath) : null)
                     ?? GetFileIcon(fallbackPath);
        }

        Cache[cacheKey] = result;
        return result;
    }

    private static ImageSource? GetShellImage(string path, int size)
    {
        object? item = null;
        try
        {
            var parsingName = path.Contains('\\') || Path.IsPathRooted(path)
                ? path
                : "shell:AppsFolder\\" + path;
            var interfaceId = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref interfaceId, out item);
            if (item is not IShellItemImageFactory factory) return null;

            var result = factory.GetImage(
                new NativeSize { Width = size, Height = size },
                SIIGBF.BiggerSizeOk | SIIGBF.IconOnly,
                out var bitmap);
            if (result != 0 || bitmap == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
    }

    private static ImageSource? GetFileIcon(string path)
    {
        try
        {
            var info = new ShFileInfo();
            var handle = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
            if (handle == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>绕过 Shell 图标缓存，直接读取 exe 内嵌图标。</summary>
    private static ImageSource? GetExeIcon(string path)
    {
        var icons = new IntPtr[1];
        try
        {
            var count = ExtractIconEx(path, 0, icons, null, 1);
            if (count == 0 || icons[0] == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icons[0],
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(icons[0]);
            }
        }
        catch
        {
            return null;
        }
    }
}
