using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using cTools.Core.Models;

namespace cTools.App.Services;

public static class ThemeService
{
    private static readonly (string Key, string Light, string Dark)[] Palette =
    {
        ("WindowBgBrush", "#F4F5F7", "#141519"),
        ("SurfaceBrush", "#FCFCFD", "#1D1F25"),
        ("SurfaceAltBrush", "#F7F8FA", "#252830"),
        ("PaletteBrush", "#F7FFFFFF", "#F21D1F25"),
        ("BorderBrush", "#E6E8EC", "#323641"),
        ("BorderStrongBrush", "#D7DBE2", "#414754"),
        ("TextBrush", "#17191C", "#F3F4F6"),
        ("SubTextBrush", "#6B7280", "#A3A9B6"),
        ("AccentBrush", "#2563EB", "#6EA8FF"),
        ("AccentHoverBrush", "#1D4ED8", "#8AB8FF"),
        ("AccentSoftBrush", "#E8EFFE", "#1C2A44"),
        ("SelectionBrush", "#EDF3FF", "#20304D"),
        ("HoverBrush", "#F1F3F7", "#2B2F38"),
        ("DangerBrush", "#D92D20", "#FF8A80")
    };

    public static void Apply(cTools.Core.Models.ThemeMode mode, Window? window = null)
    {
        var dark = mode == cTools.Core.Models.ThemeMode.Dark || (mode == cTools.Core.Models.ThemeMode.System && IsSystemDark());
        foreach (var (key, light, darkColor) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? darkColor : light);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        if (window is not null)
            WindowEffects.Apply(window, dark);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>当前应用调色板是否为深色（用于尚未拿到 AppSettings 的次级窗口）。</summary>
    public static bool IsCurrentDark()
    {
        try
        {
            if (Application.Current.Resources["WindowBgBrush"] is SolidColorBrush brush)
                return (brush.Color.R + brush.Color.G + brush.Color.B) / 3 < 128;
        }
        catch
        {
            // 资源尚未初始化时按浅色处理。
        }
        return false;
    }
}
