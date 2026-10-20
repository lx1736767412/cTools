using Microsoft.Win32;

namespace cTools.App.Services;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "cTools";
    private const string LegacyValueName = "RTools";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key is null) return false;
            if (key.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value)) return true;
            return key.GetValue(LegacyValueName) is string legacy && !string.IsNullOrWhiteSpace(legacy);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(ValueName, $"\"{exe}\" --background");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
            // 旧名称的启动项不再保留，避免继续启动旧路径。
            key.DeleteValue(LegacyValueName, false);
        }
        catch
        {
            // 开机启动失败不影响主功能。
        }
    }
}
