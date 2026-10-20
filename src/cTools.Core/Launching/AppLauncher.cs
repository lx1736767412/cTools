using System.Runtime.InteropServices;
using cTools.Core.Models;

namespace cTools.Core.Launching;

public sealed record LaunchResult(bool Success, string? Error = null);

public static class AppLauncher
{
    private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo executeInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static LaunchResult Launch(AppEntry entry, bool forceAdmin = false)
    {
        if (entry is null || !entry.Enabled) return new LaunchResult(false, "启动项不可用");
        var admin = forceAdmin || entry.RunAsAdmin;

        if (entry.Kind == EntryKind.Uwp)
        {
            if (string.IsNullOrWhiteSpace(entry.AppUserModelId))
                return new LaunchResult(false, "缺少 UWP 应用标识");
            return Execute("explorer.exe", $"shell:AppsFolder\\{entry.AppUserModelId}", null, admin);
        }

        if (entry.Kind == EntryKind.Command)
        {
            var workingDirectory = Directory.Exists(entry.WorkingDirectory) ? entry.WorkingDirectory : null;
            var command = string.IsNullOrWhiteSpace(entry.Arguments)
                ? entry.Target
                : $"{entry.Target} {entry.Arguments}";
            return Execute("cmd.exe", $"/c {command}", workingDirectory, admin);
        }

        var directory = !string.IsNullOrWhiteSpace(entry.WorkingDirectory) && Directory.Exists(entry.WorkingDirectory)
            ? entry.WorkingDirectory
            : null;
        return Execute(entry.Target, entry.Arguments, directory, admin);
    }

    public static LaunchResult OpenLocation(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return Execute("explorer.exe", $"\"{path}\"", null, false);
            if (File.Exists(path))
                return Execute("explorer.exe", $"/select,\"{path}\"", null, false);
            return new LaunchResult(false, "路径不存在");
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, ex.Message);
        }
    }

    private static LaunchResult Execute(string file, string? arguments, string? workingDirectory, bool admin)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI,
            hwnd = IntPtr.Zero,
            lpVerb = admin ? "runas" : null,
            lpFile = file,
            lpParameters = string.IsNullOrWhiteSpace(arguments) ? null : arguments,
            lpDirectory = workingDirectory,
            nShow = SW_SHOWNORMAL
        };
        try
        {
            if (!ShellExecuteExW(ref info))
            {
                var error = Marshal.GetLastWin32Error();
                var message = error == 1223 ? "已取消管理员授权" : $"启动失败（Win32 {error}）";
                return new LaunchResult(false, message);
            }
            return new LaunchResult(true);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, ex.Message);
        }
        finally
        {
            if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess);
        }
    }
}
