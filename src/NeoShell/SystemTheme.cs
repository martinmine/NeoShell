using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.UI;

namespace NeoShell;

public static class SystemTheme
{
    /// <summary>
    /// The Windows (not app) mode from Settings → Personalization → Colors, which the taskbar and Start follow.
    /// Re-read it on <c>WM_SETTINGCHANGE</c>.
    /// </summary>
    public static ElementTheme Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0 ? ElementTheme.Light : ElementTheme.Dark;
    }

    /// <summary>The accent palette's shades, by Windows' names (SystemAccentColorLight3 … SystemAccentColorDark3).</summary>
    public const int Light3 = 0, Dark2 = 5;

    /// <summary>
    /// The colour of the taskbar and Start when "Show accent color on Start and taskbar" is on, otherwise null; or
    /// another <paramref name="shade"/> of the accent then. Re-read it on <c>WM_SETTINGCHANGE</c>.
    /// </summary>
    public static Color? ReadAccent(int shade = Dark2)
    {
        using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        using RegistryKey? accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        return ParseAccent(personalize?.GetValue("ColorPrevalence"), accent?.GetValue("AccentPalette") as byte[], shade);
    }

    /// <summary>
    /// The palette holds eight RGBA colours: three lighter shades, the accent, three darker shades and one unused.
    /// Windows paints the taskbar with the second darker shade.
    /// </summary>
    public static Color? ParseAccent(object? colorPrevalence, byte[]? palette, int shade = Dark2)
    {
        int at = shade * 4;
        if (colorPrevalence is not int prevalence || prevalence == 0 || palette is null || palette.Length < at + 3)
            return null;
        return Color.FromArgb(255, palette[at], palette[at + 1], palette[at + 2]);
    }

    /// <summary>Light text on dark colours and dark text on light ones.</summary>
    public static ElementTheme ThemeOn(Color background) =>
        0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B > 140 ? ElementTheme.Light : ElementTheme.Dark;
}
