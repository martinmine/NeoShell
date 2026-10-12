using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public enum CaptionCommand { Minimize, Maximize, Restore, Close, Help }

/// <summary>
/// What a click on another app's title bar does, done for it from a window of ours lying over the title bar: the app
/// gets the same messages its own caption would give it, posted (never sent, so a hung app can't stall us), which UIPI
/// lets through from a process of the same or higher integrity.
/// </summary>
public static class CaptionInput
{
    /// <summary>A caption button's command (<c>WM_SYSCOMMAND</c>), which the app may handle itself (close to the tray).</summary>
    public static void Send(nint hwnd, CaptionCommand command) =>
        User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, command switch
        {
            CaptionCommand.Minimize => User32.SC_MINIMIZE,
            CaptionCommand.Maximize => User32.SC_MAXIMIZE,
            CaptionCommand.Restore => User32.SC_RESTORE,
            CaptionCommand.Help => User32.SC_CONTEXTHELP,
            _ => User32.SC_CLOSE,
        }, 0);

    /// <summary>
    /// A press on the caption: the app's own move loop starts at the pointer, as if the press had reached its caption.
    /// Call it on the press, while this process may still take the foreground (it got the last input).
    /// </summary>
    public static void BeginDrag(nint hwnd)
    {
        User32.SetForegroundWindow(hwnd);
        User32.ReleaseCapture();
        User32.PostMessage(hwnd, User32.WM_NCLBUTTONDOWN, User32.HTCAPTION, CursorLParam());
    }

    /// <summary>A double-click on the caption: the app maximizes or restores, if it may.</summary>
    public static void DoubleClick(nint hwnd) =>
        User32.PostMessage(hwnd, User32.WM_NCLBUTTONDBLCLK, User32.HTCAPTION, CursorLParam());

    /// <summary>
    /// A right-click on the caption: the window menu, opened as Alt+Space opens it (at the window's corner, not at the
    /// pointer: no public message opens it at a point).
    /// </summary>
    public static void OpenWindowMenu(nint hwnd)
    {
        User32.SetForegroundWindow(hwnd);
        User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, User32.SC_KEYMENU, ' ');
    }

    private static nint CursorLParam()
    {
        User32.GetCursorPos(out User32.POINT point);
        return (nint)(((point.y & 0xFFFF) << 16) | (point.x & 0xFFFF));
    }
}
