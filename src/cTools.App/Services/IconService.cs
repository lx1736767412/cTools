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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return null;

        var isUwp = path.Contains('!') && !path.Contains('\\') && !path.Contains('/');
        var cacheKey = isUwp ? "uwp:" + path : path;
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        ImageSource? result;
        if (isUwp)
        {
            result = GetUwpIcon(path);
        }
        else
        {
            if (!Path.IsPathRooted(path) && !File.Exists(path) && !Directory.Exists(path)) return null;
            result = GetFileIcon(path);
        }

        Cache[cacheKey] = result;
        return result;
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

    /// <summary>
    /// UWP 应用没有可以直接 SHGetFileInfo 的路径，改从 shell:AppsFolder 的 Shell 项取图。
    /// </summary>
    private static ImageSource? GetUwpIcon(string appUserModelId)
    {
        object? item = null;
        try
        {
            var interfaceId = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName("shell:AppsFolder\\" + appUserModelId, IntPtr.Zero, ref interfaceId, out item);
            if (item is not IShellItemImageFactory factory) return null;

            var result = factory.GetImage(
                new NativeSize { Width = 64, Height = 64 },
                SIIGBF.BiggerSizeOk,
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
}
