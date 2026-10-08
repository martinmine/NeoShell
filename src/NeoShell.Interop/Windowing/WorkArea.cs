using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public static unsafe class WorkArea
{
    /// <summary>
    /// Sets the work area (where windows maximize) of the monitor that contains <paramref name="area"/>, and fits the
    /// maximized windows there to it, as Explorer does. As the shell NeoShell does this itself; alongside Explorer,
    /// <see cref="AppBar"/> asks Explorer to.
    /// </summary>
    /// <param name="waitForWindows">
    /// Returns once every window has been told. Never on the UI thread: apps that answer by calling the shell
    /// (re-adding their tray icons) would wait for that thread while it waits for them. Otherwise windows are told
    /// without waiting for them.
    /// </param>
    public static void Set(RectInt32 area, bool waitForWindows)
    {
        User32.RECT rect = User32.RECT.From(area);
        // A nonzero uiParam (undocumented; Explorer passes TRUE) makes Windows maximize the maximized windows again
        // for the new work area: elevated ones too, hung ones once they answer, cloaked ones once they show.
        if (!User32.SystemParametersInfo(User32.SPI_SETWORKAREA, 1, &rect, waitForWindows ? User32.SPIF_SENDCHANGE : 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!waitForWindows)
            User32.SendNotifyMessage(User32.HWND_BROADCAST, WindowMessages.SettingChange, (nint)User32.SPI_SETWORKAREA, 0);
    }

    /// <summary>
    /// The work area Windows has now for the monitor with the bounds <paramref name="monitor"/>, or null when no monitor
    /// has those bounds (any more).
    /// </summary>
    public static RectInt32? Get(RectInt32 monitor)
    {
        User32.RECT bounds = User32.RECT.From(monitor);
        var info = new User32.MONITORINFO { cbSize = (uint)sizeof(User32.MONITORINFO) };
        nint handle = User32.MonitorFromRect(&bounds, User32.MONITOR_DEFAULTTONULL);
        if (handle == 0 || !User32.GetMonitorInfo(handle, &info) || info.rcMonitor.ToRectInt32() != monitor)
            return null;
        return info.rcWork.ToRectInt32();
    }
}
