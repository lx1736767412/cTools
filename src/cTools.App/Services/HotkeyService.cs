using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using cTools.Core.Logging;

namespace cTools.App.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly Window _window;
    private readonly Action _callback;
    private readonly Func<bool>? _suppress;
    private readonly int _id = 0x5254;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;
    private HookProc? _hookProc;
    private IntPtr _hook;
    private uint _modifiers;
    private uint _virtualKey;
    private long _lastTrigger;

    public HotkeyService(Window window, string hotkey, Action callback, Func<bool>? suppress = null)
    {
        _window = window;
        _callback = callback;
        _suppress = suppress;
        Hotkey = hotkey;
    }

    public string Hotkey { get; }

    public bool Register()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return false;
        if (!TryParse(Hotkey, out _modifiers, out _virtualKey)) return false;
        // 单字母/数字全局热键会吞掉正常输入；只放行带修饰键的组合或 F1–F24。
        if (_modifiers == 0 && (_virtualKey < 0x70 || _virtualKey > 0x87))
        {
            Log.Info($"拒绝注册无修饰键的全局快捷键: {Hotkey}");
            return false;
        }
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _registered = RegisterHotKey(_hwnd, _id, _modifiers | ModNoRepeat, _virtualKey);
        if (_registered)
        {
            Log.Info($"RegisterHotKey 成功: {Hotkey}");
            return true;
        }

        // Alt+Space 等系统保留组合无法用 RegisterHotKey 注册，回退到低级键盘钩子。
        _hookProc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        _registered = _hook != IntPtr.Zero;
        Log.Info(_registered
            ? $"低级键盘钩子安装成功: {Hotkey}"
            : $"低级键盘钩子安装失败: {Hotkey}, Win32={Marshal.GetLastWin32Error()}");
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _id)
        {
            if (_suppress?.Invoke() == true) return IntPtr.Zero;
            _callback();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var message = wParam.ToInt32();
        if (message is not (WmKeyDown or WmSysKeyDown))
            return CallNextHookEx(_hook, code, wParam, lParam);

        var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if (info.vkCode != _virtualKey)
            return CallNextHookEx(_hook, code, wParam, lParam);
        if (!ModifiersMatch())
            return CallNextHookEx(_hook, code, wParam, lParam);

        // 全屏屏蔽时把按键放行给前台应用，而不是吞掉。
        if (_suppress?.Invoke() == true)
            return CallNextHookEx(_hook, code, wParam, lParam);

        var now = Environment.TickCount64;
        if (now - _lastTrigger < 300) return (IntPtr)1;
        _lastTrigger = now;
        _callback();
        return (IntPtr)1;
    }

    private bool ModifiersMatch()
    {
        var alt = IsDown(0x12);
        var ctrl = IsDown(0x11);
        var shift = IsDown(0x10);
        var win = IsDown(0x5B) || IsDown(0x5C);
        return alt == ((_modifiers & ModAlt) != 0)
            && ctrl == ((_modifiers & ModControl) != 0)
            && shift == ((_modifiers & ModShift) != 0)
            && win == ((_modifiers & ModWin) != 0);
    }

    private static bool IsDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;
        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= ModAlt; break;
                case "ctrl":
                case "control": modifiers |= ModControl; break;
                case "shift": modifiers |= ModShift; break;
                case "win":
                case "windows": modifiers |= ModWin; break;
                default:
                    if (!TryParseKey(part, out var key)) return false;
                    virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return virtualKey != 0;
    }

    public static bool TryParseKey(string value, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.Length == 1 && char.IsDigit(text[0]))
        {
            key = (Key)((int)Key.D0 + (text[0] - '0'));
            return true;
        }
        if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
            && text.Length == 4 && char.IsDigit(text[3]))
        {
            key = (Key)((int)Key.NumPad0 + (text[3] - '0'));
            return true;
        }
        if (Enum.TryParse<Key>(text, true, out key)) return true;
        key = text.ToLowerInvariant() switch
        {
            "esc" => Key.Escape,
            "escape" => Key.Escape,
            "return" => Key.Enter,
            "space" => Key.Space,
            "del" => Key.Delete,
            "ins" => Key.Insert,
            "pgup" => Key.PageUp,
            "pgdn" => Key.PageDown,
            "backspace" => Key.Back,
            _ => Key.None
        };
        return key != Key.None;
    }

    /// <summary>把 WPF 键名转成配置中使用的简短名称。</summary>
    public static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + ((int)key - (int)Key.D0))).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return "Num" + ((char)('0' + ((int)key - (int)Key.NumPad0)));
        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.OemPlus => "OemPlus",
            Key.OemMinus => "OemMinus",
            Key.OemComma => "OemComma",
            Key.OemPeriod => "OemPeriod",
            _ => key.ToString()
        };
    }

    public void Dispose()
    {
        if (_registered && _hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, _id);
        _registered = false;
        _source?.RemoveHook(WndProc);
        _source = null;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _hookProc = null;
    }
}
