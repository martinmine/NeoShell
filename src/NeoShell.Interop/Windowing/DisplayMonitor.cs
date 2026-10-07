using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>A display monitor. Rectangles are in physical pixels in virtual-screen coordinates.</summary>
/// <param name="Dpi">Effective DPI: 96 at 100% scaling, 144 at 150%.</param>
/// <param name="DevicePath">
/// The monitor's device interface path (<c>\\?\DISPLAY#...</c>), which identifies it in <c>IDesktopWallpaper</c> and
/// Explorer's per-monitor wallpaper values; empty if unknown.
/// </param>
public sealed record DisplayMonitor(nint Handle, RectInt32 Bounds, RectInt32 WorkArea, bool IsPrimary, uint Dpi, string DevicePath = "")
{
    public static unsafe IReadOnlyList<DisplayMonitor> GetAll()
    {
        var monitors = new List<DisplayMonitor>();
        GCHandle handle = GCHandle.Alloc(monitors);
        try
        {
            User32.EnumDisplayMonitors(0, null, &OnMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return monitors;
    }

    /// <summary>
    /// The handle of the monitor <paramref name="rect"/> overlaps most; when it overlaps none, 0, or with
    /// <paramref name="nearest"/> the nearest monitor.
    /// </summary>
    public static unsafe nint HandleFromRect(RectInt32 rect, bool nearest = false)
    {
        User32.RECT bounds = User32.RECT.From(rect);
        return User32.MonitorFromRect(&bounds, nearest ? User32.MONITOR_DEFAULTTONEAREST : User32.MONITOR_DEFAULTTONULL);
    }

    /// <summary>The first active monitor on the display adapter output <paramref name="adapterDevice"/> (<c>\\.\DISPLAY1</c>).</summary>
    private static unsafe string GetDevicePath(char* adapterDevice)
    {
        var device = new User32.DISPLAY_DEVICE { cb = (uint)sizeof(User32.DISPLAY_DEVICE) };
        for (uint i = 0; User32.EnumDisplayDevices(adapterDevice, i, &device, User32.EDD_GET_DEVICE_INTERFACE_NAME); i++)
        {
            if ((device.StateFlags & User32.DISPLAY_DEVICE_ACTIVE) != 0)
                return new string(device.DeviceID);
        }
        return "";
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnMonitor(nint monitor, nint hdc, User32.RECT* clip, nint data)
    {
        try
        {
            var infoEx = new User32.MONITORINFOEX { info = { cbSize = (uint)sizeof(User32.MONITORINFOEX) } };
            if (User32.GetMonitorInfo(monitor, &infoEx))
            {
                User32.MONITORINFO info = infoEx.info;
                if (Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpi, out _) != 0)
                    dpi = 96;

                var monitors = (List<DisplayMonitor>)GCHandle.FromIntPtr(data).Target!;
                monitors.Add(new DisplayMonitor(
                    monitor,
                    info.rcMonitor.ToRectInt32(),
                    info.rcWork.ToRectInt32(),
                    (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                    dpi,
                    GetDevicePath(infoEx.szDevice)));
            }
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 1; // continue enumerating
    }
}
