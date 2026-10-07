using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>The calls Explorer makes to change the desktop background for the whole session.</summary>
public static unsafe class DesktopBackground
{
    /// <summary>
    /// Sets <c>Control Panel\Desktop\Wallpaper</c> through Windows, which also tells every top-level window
    /// (<c>WM_SETTINGCHANGE</c>); an empty path means no picture. Throws if Windows refuses (a missing file).
    /// </summary>
    public static void SetWallpaper(string path)
    {
        fixed (char* text = path)
        {
            if (!User32.SystemParametersInfo(User32.SPI_SETDESKWALLPAPER, 0, text, User32.SPIF_UPDATEINIFILE | User32.SPIF_SENDCHANGE))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    // Power Options → Desktop background settings → Slide show: 0 available, 1 paused (by default on battery).
    private static readonly Guid DesktopBackgroundSubgroup = new("0d7dbae2-4294-402a-ba8e-26777e8488cd");
    private static readonly Guid SlideShowSetting = new("309dce9b-bef4-4119-9921-a851fb12f0f4");

    /// <summary>
    /// Whether the power plan pauses the slideshow on the current power source (Settings' "run on battery" off), as
    /// Explorer checks it.
    /// </summary>
    public static bool IsSlideshowPaused()
    {
        if (!Kernel32.GetSystemPowerStatus(out Kernel32.SYSTEM_POWER_STATUS status)
            || PowrProf.PowerGetActiveScheme(0, out Guid* scheme) != 0)
        {
            return false;
        }

        try
        {
            uint index;
            uint result = status.ACLineStatus == 0
                ? PowrProf.PowerReadDCValueIndex(0, scheme, DesktopBackgroundSubgroup, SlideShowSetting, out index)
                : PowrProf.PowerReadACValueIndex(0, scheme, DesktopBackgroundSubgroup, SlideShowSetting, out index);
            return result == 0 && index != 0;
        }
        finally
        {
            Kernel32.LocalFree((nint)scheme);
        }
    }

    /// <summary>Sets the session's desktop colour (<c>COLOR_BACKGROUND</c>), which tells every window (<c>WM_SYSCOLORCHANGE</c>).</summary>
    /// <param name="color">A <c>COLORREF</c>: 0x00BBGGRR.</param>
    public static void SetColor(uint color)
    {
        int element = User32.COLOR_BACKGROUND;
        if (!User32.SetSysColors(1, &element, &color))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
