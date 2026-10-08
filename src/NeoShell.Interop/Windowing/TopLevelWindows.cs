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

    /// <summary>True for a window the user can resize, which Snap may then place: it has a sizing border.</summary>
    public static bool CanResize(nint hwnd) => ((uint)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE) & User32.WS_THICKFRAME) != 0;

    public static bool IsMinimized(nint hwnd) => User32.IsIconic(hwnd);

    /// <summary>On screen: visible, not minimized and not cloaked (e.g. on another virtual desktop).</summary>
    public static bool IsOnScreen(nint hwnd) => User32.IsWindowVisible(hwnd) && !User32.IsIconic(hwnd) && !IsCloaked(hwnd);

    public static bool IsMaximized(nint hwnd) => User32.IsZoomed(hwnd);

    public static bool Exists(nint hwnd) => User32.IsWindow(hwnd);

    private static readonly uint s_taskbarButtonCreated = User32.RegisterWindowMessage("TaskbarButtonCreated");

    /// <summary>
    /// Tells a window its taskbar button exists. Apps wait for this before using <c>ITaskbarList3</c> (progress,
    /// badges). Sent without waiting, so a hung app can't hold the taskbar up.
    /// </summary>
    public static void NotifyButtonCreated(nint hwnd) => User32.SendNotifyMessage(hwnd, s_taskbarButtonCreated, 0, 0);

    public static nint GetForeground() => User32.GetForegroundWindow();

    /// <summary>The window's owner's owner and so on: the app window an owned window (a dialog) belongs to.</summary>
    public static nint RootOwner(nint hwnd) => User32.GetAncestor(hwnd, User32.GA_ROOTOWNER);

    /// <summary>
    /// Whether a title bar shake or Win+Home may keep this window up and minimize the rest, as Explorer checks it
    /// (uxtheme's own test): an app's top-level window, not the desktop or the taskbar.
    /// </summary>
    public static bool IsShakable(nint hwnd) => hwnd != 0 && UxTheme.IsValidShakeWindow(hwnd);

    /// <summary>The top-level window under the screen point, or 0.</summary>
    public static nint RootAt(PointInt32 point)
    {
        nint hwnd = User32.WindowFromPoint(new User32.POINT { x = point.X, y = point.Y });
        return hwnd == 0 ? 0 : User32.GetAncestor(hwnd, User32.GA_ROOT);
    }

    /// <summary>
    /// The maximize button of a window with Windows' own caption buttons (DWM draws them), in screen pixels: the middle
    /// of the three 46 effective pixel buttons at the right of <c>DWMWA_CAPTION_BUTTON_BOUNDS</c>. Null for a window
    /// without them, or whose own title bar covers them (its client area reaches over the caption).
    /// </summary>
    /// <remarks>
    /// Not <c>WM_NCHITTEST</c>: <c>DefWindowProc</c> answers it with the caption's old metrics, which put the buttons
    /// a good 20 pixels right of where Windows 11 draws them.
    /// </remarks>
    public static RectInt32? CaptionMaximizeButton(nint hwnd)
    {
        if (((uint)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE) & User32.WS_MAXIMIZEBOX) == 0)
            return null;
        User32.RECT buttons;
        if (Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CAPTION_BUTTON_BOUNDS, &buttons, (uint)sizeof(User32.RECT)) != 0
            || buttons.right <= buttons.left || buttons.bottom <= buttons.top)
        {
            return null;
        }
        RectInt32 window = GetBounds(hwnd);
        var client = new User32.POINT();
        if (!User32.ClientToScreen(hwnd, ref client) || client.y < window.Y + buttons.bottom)
            return null;
        uint dpi = User32.GetDpiForWindow(hwnd);
        int width = (int)Math.Round(46 * (dpi == 0 ? 96 : dpi) / 96.0);
        return new RectInt32(window.X + buttons.right - 2 * width, window.Y + buttons.top, width, buttons.bottom - buttons.top);
    }

    /// <summary>
    /// Hides the tooltip Windows shows over a window's caption button ("Maximize"): with Explorer, Windows leaves it
    /// out over the maximize button, where Snap layouts show instead.
    /// </summary>
    public static void HideCaptionTooltip(nint hwnd)
    {
        const int SW_HIDE = 0;
        uint thread = User32.GetWindowThreadProcessId(hwnd, out _);
        for (nint tip = User32.FindWindowEx(0, 0, "MicrosoftWindowsTooltip", null); tip != 0;
            tip = User32.FindWindowEx(0, tip, "MicrosoftWindowsTooltip", null))
        {
            if (User32.IsWindowVisible(tip) && User32.GetWindowThreadProcessId(tip, out _) == thread)
                User32.ShowWindowAsync(tip, SW_HIDE);
        }
    }

    /// <summary>
    /// Whether the window says the screen point is on its maximize button (<c>WM_NCHITTEST</c>'s <c>HTMAXBUTTON</c>),
    /// as Windows 11 asks windows with their own title bars (WinUI, Chromium, Electron) to, for Snap layouts; so may
    /// the child window under the point. A hung window, or one of an app running as administrator (UIPI), says nothing.
    /// </summary>
    public static bool AnswersMaximizeButton(nint hwnd, PointInt32 point)
    {
        nint lParam = (nint)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
        if (Answers(hwnd, lParam))
            return true;
        nint child = User32.WindowFromPoint(new User32.POINT { x = point.X, y = point.Y });
        return child != 0 && child != hwnd && Answers(child, lParam);

        static bool Answers(nint target, nint lParam) =>
            User32.SendMessageTimeout(target, User32.WM_NCHITTEST, 0, lParam, User32.SMTO_ABORTIFHUNG, 100, out nint result) != 0
            && result == User32.HTMAXBUTTON;
    }

    /// <summary>The window's rectangle on screen, in pixels.</summary>
    public static RectInt32 GetBounds(nint hwnd) => User32.GetWindowRect(hwnd, out User32.RECT rect) ? rect.ToRectInt32() : default;

    /// <summary>
    /// The part of the window that's drawn, in pixels: without the invisible resize borders Windows 10 and 11 put
    /// around most windows, which <see cref="GetBounds"/> counts in.
    /// </summary>
    public static RectInt32 GetVisibleBounds(nint hwnd)
    {
        User32.RECT frame;
        return Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(User32.RECT)) == 0
            ? frame.ToRectInt32()
            : GetBounds(hwnd);
    }

    /// <summary>
    /// Moves and sizes the window so the part that's drawn fills <paramref name="bounds"/> exactly, as Snap does,
    /// restoring it first if it's maximized or minimized.
    /// </summary>
    public static void Place(nint hwnd, RectInt32 bounds)
    {
        if (User32.IsZoomed(hwnd) || User32.IsIconic(hwnd))
            Show(hwnd, User32.SW_RESTORE, User32.SC_RESTORE, wait: true);

        // A window that isn't aware of each monitor's DPI sees each monitor scaled to its own scale, and is placed in
        // those coordinates: in screen pixels Windows scales it again on a monitor at another scale, or rounds it a
        // pixel short where it reaches one.
        RectInt32 visible = GetVisibleBounds(hwnd);
        nint context = User32.GetWindowDpiAwarenessContext(hwnd);
        bool scaled = User32.GetAwarenessFromDpiAwarenessContext(context) != User32.DPI_AWARENESS_PER_MONITOR_AWARE;
        if (scaled)
        {
            visible = ScaledFor(visible, context);
            bounds = ScaledFor(bounds, context);
        }
        nint previous = scaled ? User32.SetThreadDpiAwarenessContext(context) : 0;
        try
        {
            // The invisible borders stay outside the bounds, as wide as they are now.
            RectInt32 outer = GetBounds(hwnd);
            int left = visible.X - outer.X;
            int top = visible.Y - outer.Y;
            int right = outer.X + outer.Width - (visible.X + visible.Width);
            int bottom = outer.Y + outer.Height - (visible.Y + visible.Height);
            User32.SetWindowPos(hwnd, 0, bounds.X - left, bounds.Y - top, bounds.Width + left + right, bounds.Height + top + bottom,
                User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
        finally
        {
            if (scaled)
                User32.SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>
    /// A rectangle in screen pixels as a thread of this DPI awareness sees it: its monitor scaled to the monitor as that
    /// thread sees it.
    /// </summary>
    private static RectInt32 ScaledFor(RectInt32 rect, nint context)
    {
        User32.RECT r = User32.RECT.From(rect);
        nint monitor = User32.MonitorFromRect(&r, User32.MONITOR_DEFAULTTONEAREST);
        var physical = new User32.MONITORINFO { cbSize = (uint)sizeof(User32.MONITORINFO) };
        var logical = physical;
        nint previous = User32.SetThreadDpiAwarenessContext(context);
        bool known = User32.GetMonitorInfo(monitor, &logical);
        User32.SetThreadDpiAwarenessContext(previous);
        if (!known || !User32.GetMonitorInfo(monitor, &physical))
            return rect;

        RectInt32 from = physical.rcMonitor.ToRectInt32(), to = logical.rcMonitor.ToRectInt32();
        int left = Scale(rect.X - from.X, to.Width, from.Width) + to.X;
        int top = Scale(rect.Y - from.Y, to.Height, from.Height) + to.Y;
        int right = Scale(rect.X + rect.Width - from.X, to.Width, from.Width) + to.X;
        int bottom = Scale(rect.Y + rect.Height - from.Y, to.Height, from.Height) + to.Y;
        return new RectInt32(left, top, right - left, bottom - top);

        static int Scale(int value, int by, int over) => (int)Math.Round(value * (double)by / over, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// <see cref="Place"/> for a window that isn't to come to the front (Snap Assist keeps the keyboard while it fills
    /// the next zone): a minimized or maximized one is restored without activating it. A window of an app running as
    /// administrator refuses (UIPI) and is left as it is.
    /// </summary>
    public static void PlaceInBackground(nint hwnd, RectInt32 bounds)
    {
        if (User32.IsZoomed(hwnd) || User32.IsIconic(hwnd))
        {
            var placement = new User32.WINDOWPLACEMENT { length = (uint)sizeof(User32.WINDOWPLACEMENT) };
            if (!User32.GetWindowPlacement(hwnd, &placement))
                return;
            placement.flags &= ~User32.WPF_RESTORETOMAXIMIZED;
            placement.showCmd = User32.SW_SHOWNOACTIVATE;
            if (!User32.SetWindowPlacement(hwnd, &placement))
                return;
        }
        Place(hwnd, bounds);
    }

    /// <summary>
    /// How wide a standard window's resize border is at <paramref name="dpi"/>, the visible pixel of it included: its
    /// invisible border is one less (7 at 96 DPI, 8 at 120).
    /// </summary>
    public static int ResizeBorder(uint dpi) =>
        User32.GetSystemMetricsForDpi(User32.SM_CXSIZEFRAME, dpi) + User32.GetSystemMetricsForDpi(User32.SM_CXPADDEDBORDER, dpi);

    /// <summary>Moves and sizes the window, invisible borders included (as <see cref="GetBounds"/> measures it).</summary>
    public static void SetBounds(nint hwnd, RectInt32 bounds) =>
        User32.SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);

    /// <summary>Maximizes the window, before returning.</summary>
    public static void Maximize(nint hwnd) => Show(hwnd, User32.SW_MAXIMIZE, User32.SC_MAXIMIZE, wait: true);

    /// <summary>Restores a maximized or minimized window, before returning.</summary>
    public static void RestoreNow(nint hwnd) => Show(hwnd, User32.SW_RESTORE, User32.SC_RESTORE, wait: true);

    /// <summary>The monitor the window is mostly on (compare with <see cref="DisplayMonitor.Handle"/>), or 0.</summary>
    public static nint MonitorOf(nint hwnd) => User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONULL);

    /// <summary>The monitor the window is mostly on, or the nearest one when it's on none.</summary>
    public static nint NearestMonitorOf(nint hwnd) => User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);

    /// <summary>Puts the window on top of its band (topmost or not) without activating it.</summary>
    public static void BringToTop(nint hwnd) =>
        User32.SetWindowPos(hwnd, User32.HWND_TOP, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

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
            Show(hwnd, User32.SW_RESTORE, User32.SC_RESTORE);
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
    public static void MinimizeAndActivateNext(nint hwnd) => Show(hwnd, User32.SW_MINIMIZE, User32.SC_MINIMIZE);

    /// <summary>
    /// Tells the window a button of its thumbnail toolbar was clicked, as Explorer does: <c>WM_COMMAND</c> with
    /// <c>THBN_CLICKED</c> and the button's ID.
    /// </summary>
    public static void ClickThumbButton(nint hwnd, uint id)
    {
        const uint THBN_CLICKED = 0x1800;
        User32.PostMessage(hwnd, User32.WM_COMMAND, (nint)((THBN_CLICKED << 16) | (id & 0xFFFF)), 0);
    }

    /// <summary>Asks the window to close, as its title bar's close button would; it may ask to save first.</summary>
    public static void Close(nint hwnd) => User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, User32.SC_CLOSE, 0);

    /// <summary>
    /// Ends the window's process at once, without asking it to close: Explorer's "End task". The window manager
    /// (CSRSS) ends it, so hung and elevated apps end too. Only that process: its children keep running. Blocks
    /// while CSRSS works, so call it off the UI thread.
    /// </summary>
    public static void EndTask(nint hwnd) => User32.EndTask(hwnd, shutDown: false, force: true);

    /// <summary>Minimizes without activating the next window, so nothing flickers to the front.</summary>
    public static void Minimize(nint hwnd) => Show(hwnd, User32.SW_SHOWMINNOACTIVE, User32.SC_MINIMIZE);

    /// <param name="activate">False restores it without activating it: the window in front stays in front.</param>
    /// <param name="below">A window to put it just below, rather than on top of the others.</param>
    public static void Restore(nint hwnd, bool activate = true, nint below = 0)
    {
        Show(hwnd, activate ? User32.SW_RESTORE : User32.SW_SHOWNOACTIVATE, User32.SC_RESTORE);
        // Queued for the window's thread too, so after the restore.
        if (below != 0)
        {
            User32.SetWindowPos(hwnd, below, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_ASYNCWINDOWPOS);
        }
    }

    /// <summary>
    /// Minimizes, restores or maximizes a window. Windows refuses that for a window of an app running as administrator
    /// (UIPI: NeoShell runs at a lower integrity level), so it gets the system menu's command instead, which passes, as
    /// if the user chose it there (a minimized one then activates the next window, a restored one itself). Without it,
    /// an elevated window minimized couldn't be brought back from the taskbar.
    /// </summary>
    /// <param name="wait">Done before returning, not queued for the window's thread (the command is queued either way).</param>
    private static void Show(nint hwnd, int show, nint systemCommand, bool wait = false)
    {
        const int ERROR_ACCESS_DENIED = 5;
        bool done = wait ? User32.ShowWindow(hwnd, show) : User32.ShowWindowAsync(hwnd, show);
        if (!done && Marshal.GetLastPInvokeError() == ERROR_ACCESS_DENIED)
            User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, systemCommand, 0);
    }

    public static int GetProcessId(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint processId);
        return (int)processId;
    }

    /// <summary>
    /// Hides or shows the window on screen through DWM while it stays shown to Windows (and its content keeps being
    /// drawn): a window can draw itself there before it appears.
    /// </summary>
    public static void Cloak(nint hwnd, bool cloak)
    {
        int value = cloak ? 1 : 0;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAK, &value, sizeof(int));
    }

    internal static bool IsCloaked(nint hwnd)
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
