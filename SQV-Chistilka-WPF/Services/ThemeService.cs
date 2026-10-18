using System.Runtime.InteropServices;
using System.Windows.Media;

namespace SqvChistilka.Services;

public static class ThemeService
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetColorizationColor(out uint color, out bool opaque);

    public static string GetAccentHex(string fallback = "#Eaff00")
    {
        try
        {
            if (DwmGetColorizationColor(out uint c, out _) == 0)
                return $"#{(c & 0x00FFFFFF):X6}";
        }
        catch { }
        return fallback;
    }

    public static Color GetAccentColor()
    {
        try
        {
            string hex = GetAccentHex();
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch { return (Color)ColorConverter.ConvertFromString("#Eaff00"); }
    }
}
