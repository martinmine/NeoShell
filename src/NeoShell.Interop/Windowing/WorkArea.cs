using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public static unsafe class WorkArea
{
    /// <summary>
    /// Sets the work area (where windows maximize) of the monitor that contains <paramref name="area"/>, and resizes
    /// the maximized windows on it. As the shell NeoShell does this itself; alongside Explorer, <see cref="AppBar"/>
    /// asks Explorer to.
    /// </summary>
    /// <remarks>
    /// Returns once every window has been told, so never call it on the UI thread: apps that answer by calling the
    /// shell (re-adding their tray icons) would wait for that thread while it waits for them.
    /// </remarks>
    public static void Set(RectInt32 area)
    {
        User32.RECT rect = User32.RECT.From(area);
        if (!User32.SystemParametersInfo(User32.SPI_SETWORKAREA, 0, &rect, User32.SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
