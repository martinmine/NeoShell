using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Tray;

/// <summary>
/// The pictures of a balloon notification shown as a toast, loaded as Explorer's taskbar loads them: sharp versions
/// from the icons' own files rather than the small icons apps hand to the tray.
/// </summary>
public static unsafe class BalloonIcons
{
    /// <summary>
    /// The picture beside the balloon's text: the stock information, warning or error icon, the balloon's own icon
    /// (<see cref="BalloonFlags.User"/>), or null for none.
    /// </summary>
    /// <param name="balloonIcon">The balloon's own icon, or 0 to use <paramref name="trayIcon"/>.</param>
    public static IconBitmap? Load(BalloonFlags flags, nint balloonIcon, nint trayIcon, int size) => (flags & BalloonFlags.IconMask) switch
    {
        BalloonFlags.Info => Stock(Shell32.SIID_INFO, size),
        BalloonFlags.Warning => Stock(Shell32.SIID_WARNING, size),
        BalloonFlags.Error => Stock(Shell32.SIID_ERROR, size),
        BalloonFlags.User => Sharp(balloonIcon != 0 ? balloonIcon : trayIcon, size),
        _ => null,
    };

    /// <summary>
    /// An icon at <paramref name="size"/> pixels, loaded again from the file and resource it came from when Windows
    /// knows them (<c>GetIconInfoEx</c>), else the icon's own pixels.
    /// </summary>
    public static IconBitmap? Sharp(nint icon, int size)
    {
        if (icon == 0)
            return null;

        var info = new User32.ICONINFOEX { cbSize = (uint)sizeof(User32.ICONINFOEX) };
        if (User32.GetIconInfoEx(icon, &info))
        {
            Gdi32.DeleteObject(info.hbmColor);
            Gdi32.DeleteObject(info.hbmMask);
            // Only icons loaded by resource ID can be found again; a negative index is a resource ID.
            if (info.wResID != 0 && info.szModName[0] != 0 && Extract(info.szModName, -info.wResID, size) is { } sharp)
                return sharp;
        }
        return IconBitmap.FromIcon(icon);
    }

    private static IconBitmap? Stock(uint id, int size)
    {
        var info = new Shell32.SHSTOCKICONINFO { cbSize = (uint)sizeof(Shell32.SHSTOCKICONINFO) };
        return Shell32.SHGetStockIconInfo(id, Shell32.SHGSI_ICONLOCATION, &info) == 0 ? Extract(info.szPath, info.iIcon, size) : null;
    }

    private static IconBitmap? Extract(char* file, int index, int size)
    {
        nint icon;
        if (Shell32.SHDefExtractIcon(file, index, 0, &icon, null, (uint)size) != 0 || icon == 0)
            return null;
        try
        {
            return IconBitmap.FromIcon(icon);
        }
        finally
        {
            User32.DestroyIcon(icon);
        }
    }
}
