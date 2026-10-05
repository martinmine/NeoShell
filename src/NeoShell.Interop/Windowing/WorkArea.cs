using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public static unsafe class WorkArea
{
    /// <summary>
    /// Sets the work area (where windows maximize) of the monitor that contains <paramref name="area"/>. As the shell
    /// NeoShell does this itself; alongside Explorer, <see cref="AppBar"/> asks Explorer to.
    /// </summary>
    /// <param name="waitForWindows">
    /// Returns once every window has been told. Never on the UI thread: apps that answer by calling the shell
    /// (re-adding their tray icons) would wait for that thread while it waits for them. Otherwise windows are told
    /// without waiting for them.
    /// </param>
    public static void Set(RectInt32 area, bool waitForWindows)
    {
        User32.RECT rect = User32.RECT.From(area);
        if (!User32.SystemParametersInfo(User32.SPI_SETWORKAREA, 0, &rect, waitForWindows ? User32.SPIF_SENDCHANGE : 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!waitForWindows)
            User32.SendNotifyMessage(User32.HWND_BROADCAST, WindowMessages.SettingChange, (nint)User32.SPI_SETWORKAREA, 0);
    }
}
