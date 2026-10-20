using System.Runtime.InteropServices;
using System.Text;

namespace cTools.Core.Scanning;

public sealed record ShellLinkInfo(
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string IconPath,
    int IconIndex);

public static class ShellLink
{
    private const uint SLR_NO_UI = 0x00000001;
    private const uint SLR_UPDATE = 0x00000004;
    private const uint SLR_NOSEARCH = 0x00000010;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    public static ShellLinkInfo? Resolve(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        object? linkObject = null;
        try
        {
            linkObject = new ShellLinkCoClass();
            var link = (IShellLinkW)linkObject;
            var persist = (IPersistFile)linkObject;
            persist.Load(shortcutPath, 0);
            link.Resolve(IntPtr.Zero, SLR_NO_UI | SLR_UPDATE | SLR_NOSEARCH);

            var target = Read((sb, cap) => link.GetPath(sb, cap, IntPtr.Zero, 0));
            var arguments = Read(link.GetArguments);
            var workingDirectory = Read(link.GetWorkingDirectory);
            var iconBuilder = new StringBuilder(1024);
            link.GetIconLocation(iconBuilder, iconBuilder.Capacity, out var iconIndex);
            var iconPath = iconBuilder.ToString();
            if (iconPath.Contains(','))
                iconPath = iconPath[..iconPath.LastIndexOf(',')];
            if (!string.IsNullOrWhiteSpace(iconPath) && !File.Exists(iconPath))
                iconPath = "";

            return new ShellLinkInfo(target, arguments, workingDirectory, iconPath, iconIndex);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (linkObject is not null && Marshal.IsComObject(linkObject))
                Marshal.FinalReleaseComObject(linkObject);
        }
    }

    private static string Read(Action<StringBuilder, int> read)
    {
        var builder = new StringBuilder(2048);
        read(builder, builder.Capacity);
        return builder.ToString().Trim();
    }
}
