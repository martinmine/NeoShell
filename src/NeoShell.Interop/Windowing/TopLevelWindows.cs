using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

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

    public static bool IsMaximized(nint hwnd) => User32.IsZoomed(hwnd);

    public static bool Exists(nint hwnd) => User32.IsWindow(hwnd);

    private static readonly uint s_taskbarButtonCreated = User32.RegisterWindowMessage("TaskbarButtonCreated");

    /// <summary>
    /// Tells a window its taskbar button exists. Apps wait for this before using <c>ITaskbarList3</c> (progress,
    /// badges). Sent without waiting, so a hung app can't hold the taskbar up.
    /// </summary>
    public static void NotifyButtonCreated(nint hwnd) => User32.SendNotifyMessage(hwnd, s_taskbarButtonCreated, 0, 0);

    public static nint GetForeground() => User32.GetForegroundWindow();

    /// <summary>The window's rectangle on screen, in pixels.</summary>
    public static RectInt32 GetBounds(nint hwnd) => User32.GetWindowRect(hwnd, out User32.RECT rect) ? rect.ToRectInt32() : default;

    /// <summary>The monitor the window is mostly on (compare with <see cref="DisplayMonitor.Handle"/>), or 0.</summary>
    public static nint MonitorOf(nint hwnd) => User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONULL);

    /// <summary>
    /// Whether the window is the desktop: the shell window, or Explorer's desktop windows (which cover the screen but
    /// aren't full-screen apps).
    /// </summary>
    public static bool IsDesktop(nint hwnd)
    {
        if (hwnd == User32.GetShellWindow())
            return true;

        char* name = stackalloc char[32];
        string className = new(name, 0, User32.GetClassName(hwnd, name, 32));
        return className is "Progman" or "WorkerW";
    }

    /// <summary>
    /// Brings a window to the front, restoring it if minimized. Windows allows this because the user's click on the
    /// taskbar was the last input.
    /// </summary>
    public static void Activate(nint hwnd)
    {
        if (User32.IsIconic(hwnd))
            User32.ShowWindowAsync(hwnd, User32.SW_RESTORE);
        User32.SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Brings a window to the front as Alt+Tab does, restoring it if minimized. The keys went to the app in front, not
    /// to NeoShell, so Windows' foreground lock would refuse: a key of NeoShell's own (one no keyboard has) makes it the
    /// source of the last input, which may take the foreground.
    /// </summary>
    /// <remarks>
    /// Not <c>SwitchToThisWindow</c>: that sends the window left behind to the bottom of the stack, out of the most
    /// recently used order the next Alt+Tab goes by.
    /// </remarks>
    public static void SwitchTo(nint hwnd)
    {
        KeyboardHook.MaskModifierKeys();
        Activate(hwnd);
    }

    /// <summary>Minimizes and activates the next window, as clicking the active window's taskbar button does.</summary>
    public static void MinimizeAndActivateNext(nint hwnd) => User32.ShowWindowAsync(hwnd, User32.SW_MINIMIZE);

    /// <summary>Asks the window to close, as its title bar's close button would; it may ask to save first.</summary>
    public static void Close(nint hwnd) => User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, User32.SC_CLOSE, 0);

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
