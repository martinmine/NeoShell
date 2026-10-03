using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public static unsafe class TopLevelWindows
{
    /// <summary>All top-level windows in z-order, topmost first.</summary>
    public static IReadOnlyList<nint> GetAll()
    {
        var windows = new List<nint>();
        GCHandle handle = GCHandle.Alloc(windows);
        try
        {
            User32.EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    /// <summary>
    /// True for a window the user sees and could minimize: visible, not cloaked (e.g. on another virtual desktop),
    /// not minimized, with a minimize box, and not a tool window.
    /// </summary>
    public static bool CanMinimize(nint hwnd)
    {
        if (!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd))
            return false;

        var style = (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE);
        var exStyle = (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        return (style & User32.WS_MINIMIZEBOX) != 0
            && (exStyle & User32.WS_EX_TOOLWINDOW) == 0
            && !IsCloaked(hwnd);
    }

    public static bool IsMinimized(nint hwnd) => User32.IsIconic(hwnd);

    /// <summary>Minimizes without activating the next window, so nothing flickers to the front.</summary>
    public static void Minimize(nint hwnd) => User32.ShowWindowAsync(hwnd, User32.SW_SHOWMINNOACTIVE);

    public static void Restore(nint hwnd) => User32.ShowWindowAsync(hwnd, User32.SW_RESTORE);

    public static int GetProcessId(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint processId);
        return (int)processId;
    }

    private static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint data)
    {
        try
        {
            ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(hwnd);
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 1; // continue enumerating
    }
}
