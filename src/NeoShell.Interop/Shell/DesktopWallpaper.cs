using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>The wallpaper Windows shows now (<c>SPI_GETDESKWALLPAPER</c>).</summary>
public static unsafe class DesktopWallpaper
{
    // Wallpaper paths are kept to MAX_PATH by Windows; room to spare.
    private const int BufferLength = 1024;

    /// <summary>
    /// The image's full path, empty for none, or null if it can't be read. This is Windows' live setting, the one
    /// Explorer draws: a wallpaper set without being saved to the profile (<c>SPI_SETDESKWALLPAPER</c> without
    /// <c>SPIF_UPDATEINIFILE</c>) leaves the previous one in <c>Control Panel\Desktop\Wallpaper</c>.
    /// </summary>
    public static string? CurrentPath()
    {
        char* buffer = stackalloc char[BufferLength];
        return User32.SystemParametersInfo(User32.SPI_GETDESKWALLPAPER, BufferLength, buffer, 0) ? new string(buffer) : null;
    }
}
