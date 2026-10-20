using System.Runtime.InteropServices;
using System.Windows;
using cTools.Core.Models;

namespace cTools.App.Services;

public sealed record MonitorDescriptor(
    string DeviceName,
    string Label,
    Rect WorkArea,
    Rect Bounds,
    int PixelWidth,
    int PixelHeight,
    bool IsPrimary,
    double Scale);

/// <summary>枚举显示器，并把物理像素换算成 WPF 使用的 DIP 坐标。</summary>
public static class MonitorService
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorDefaultToPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect32 rcMonitor;
        public Rect32 rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect32 rcMonitor;
        public Rect32 rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point32 point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point32 point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private sealed record RawMonitor(IntPtr Handle, string DeviceName, Rect32 Bounds, Rect32 Work, bool IsPrimary);

    public static IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var raw = Enumerate();
        var result = new List<MonitorDescriptor>(raw.Count);
        for (var index = 0; index < raw.Count; index++)
            result.Add(Describe(raw[index], index));
        return result;
    }

    public static MonitorDescriptor GetTarget(AppSettings settings, IntPtr windowHandle)
    {
        var raw = Enumerate();
        if (raw.Count == 0) return Fallback();

        RawMonitor? selected = settings.PreferredMonitor switch
        {
            MonitorPreference.Primary => FromHandle(MonitorFromPoint(new Point32(), MonitorDefaultToPrimary)),
            MonitorPreference.FocusedWindow => FromWindow(GetForegroundWindow()),
            MonitorPreference.Fixed => FindByDevice(settings.PreferredMonitorDevice),
            _ => FromCursor()
        };

        // 指定显示器被拔掉 / 配置了不存在的设备时，退回鼠标所在屏幕。
        selected ??= FromCursor() ?? raw.FirstOrDefault(m => m.IsPrimary) ?? raw[0];
        var index = raw.IndexOf(selected);
        return Describe(selected, Math.Max(0, index));

        RawMonitor? FromCursor()
        {
            if (!GetCursorPos(out var point)) return null;
            return FromHandle(MonitorFromPoint(point, MonitorDefaultToNearest));
        }

        RawMonitor? FromWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return FromCursor();
            return FromHandle(MonitorFromWindow(window, MonitorDefaultToNearest));
        }

        RawMonitor? FromHandle(IntPtr handle)
            => handle == IntPtr.Zero ? null : raw.FirstOrDefault(m => m.Handle == handle);

        RawMonitor? FindByDevice(string? device)
        {
            if (string.IsNullOrWhiteSpace(device)) return null;
            return raw.FirstOrDefault(m => string.Equals(m.DeviceName, device, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static MonitorDescriptor Describe(RawMonitor monitor, int index)
    {
        var scale = GetScale(monitor.Handle);
        var width = monitor.Bounds.Width;
        var height = monitor.Bounds.Height;
        var label = $"显示器 {index + 1} · {width}×{height}" + (monitor.IsPrimary ? " · 主屏" : "");
        return new MonitorDescriptor(
            monitor.DeviceName,
            label,
            ToDip(monitor.Work, scale),
            ToDip(monitor.Bounds, scale),
            width,
            height,
            monitor.IsPrimary,
            scale);
    }

    private static Rect ToDip(Rect32 rect, double scale)
        => new(rect.Left / scale, rect.Top / scale, rect.Width / scale, rect.Height / scale);

    private static double GetScale(IntPtr monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch
        {
            // 旧系统没有 Shcore.dll 时继续使用窗口 DPI。
        }
        try
        {
            var dpi = GetDpiForSystem();
            if (dpi > 0) return dpi / 96.0;
        }
        catch
        {
            // 忽略，使用 100%。
        }
        return 1.0;
    }

    private static List<RawMonitor> Enumerate()
    {
        var list = new List<RawMonitor>(2);
        try
        {
            MonitorEnumProc callback = (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    list.Add(new RawMonitor(
                        monitor,
                        info.szDevice ?? "",
                        info.rcMonitor,
                        info.rcWork,
                        (info.dwFlags & 1) != 0));
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        catch
        {
            // 枚举失败时由调用方退回 WPF 主屏尺寸。
        }
        return list;
    }

    private static MonitorDescriptor Fallback()
    {
        var width = (int)SystemParameters.PrimaryScreenWidth;
        var height = (int)SystemParameters.PrimaryScreenHeight;
        return new MonitorDescriptor(
            "",
            "主显示器",
            new Rect(0, 0, width, height),
            new Rect(0, 0, width, height),
            width,
            height,
            true,
            1.0);
    }
}
