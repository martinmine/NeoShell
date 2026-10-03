using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>A display monitor. Rectangles are in physical pixels in virtual-screen coordinates.</summary>
/// <param name="Dpi">Effective DPI: 96 at 100% scaling, 144 at 150%.</param>
public sealed record DisplayMonitor(nint Handle, RectInt32 Bounds, RectInt32 WorkArea, bool IsPrimary, uint Dpi)
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

    [UnmanagedCallersOnly]
    private static unsafe int OnMonitor(nint monitor, nint hdc, User32.RECT* clip, nint data)
    {
        try
        {
            var info = new User32.MONITORINFO { cbSize = (uint)sizeof(User32.MONITORINFO) };
            if (User32.GetMonitorInfo(monitor, &info))
            {
                if (Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpi, out _) != 0)
                    dpi = 96;

                var monitors = (List<DisplayMonitor>)GCHandle.FromIntPtr(data).Target!;
                monitors.Add(new DisplayMonitor(
                    monitor,
                    info.rcMonitor.ToRectInt32(),
                    info.rcWork.ToRectInt32(),
                    (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                    dpi));
            }
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 1; // continue enumerating
    }
}
