using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace NeoShell;

internal static class SystemTheme
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
}
