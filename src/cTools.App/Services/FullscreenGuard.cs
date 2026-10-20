using System.Runtime.InteropServices;
using System.Text;

namespace cTools.App.Services;

/// <summary>
/// 判断当前是否处于全屏 / 演示状态。用于“全屏时屏蔽快捷键”。
/// 同时参考 Windows 通知状态和前台窗口是否覆盖整块显示器，避免漏判独占全屏游戏。
/// </summary>
public static class FullscreenGuard
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect32 rcMonitor;
        public Rect32 rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    public static bool IsSuppressed(IntPtr ownWindow)
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                // QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4
                if (state is 3 or 4) return true;
            }
        }
        catch
        {
            // shell 状态不可用时继续使用窗口矩形判断。
        }

        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == ownWindow) return false;
            if (!IsWindowVisible(foreground) || IsIconic(foreground)) return false;
            if (IsShellWindow(foreground)) return false;
            if (!GetWindowRect(foreground, out var window)) return false;

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            // 某些窗口（例如带透明边框的全屏窗口）会略微超出显示器，容差 2px。
            return window.Left <= info.rcMonitor.Left + 2
                && window.Top <= info.rcMonitor.Top + 2
                && window.Right >= info.rcMonitor.Right - 2
                && window.Bottom >= info.rcMonitor.Bottom - 2;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsShellWindow(IntPtr window)
    {
        var buffer = new StringBuilder(64);
        if (GetClassName(window, buffer, buffer.Capacity) == 0) return false;
        return buffer.ToString() switch
        {
            "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" => true,
            _ => false
        };
    }
}
