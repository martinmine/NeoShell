using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Keeps a window free of any frame. A borderless WinUI presenter still leaves a dialog frame (and puts it back
/// whenever the style is changed). Windows 11 also gives every top-level window a 1px border and rounded corners:
/// kept for popups like Start, removed for surfaces that sit flush with the screen edge.
/// </summary>
public sealed unsafe class FramelessWindow : IDisposable
{
    private const uint WS_POPUP = 0x8000_0000;
    private const uint WS_FRAME = 0x00C0_0000 /* WS_CAPTION */ | 0x0004_0000 /* WS_THICKFRAME */ | 0x0008_0000 /* WS_SYSMENU */;
    private const uint WS_EX_FRAME = 0x0000_0001 /* WS_EX_DLGMODALFRAME */ | 0x0000_0100 /* WS_EX_WINDOWEDGE */
        | 0x0000_0200 /* WS_EX_CLIENTEDGE */ | 0x0002_0000 /* WS_EX_STATICEDGE */;

    private readonly WindowSubclass _subclass;

    public FramelessWindow(nint hwnd, bool roundedCorners = false)
    {
        _subclass = new WindowSubclass(hwnd, OnMessage);

        // Rewriting the styles sends WM_STYLECHANGING, where OnMessage removes the frame.
        User32.SetWindowLongPtr(hwnd, User32.GWL_STYLE, User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE));
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE));
        User32.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);

        if (roundedCorners)
            return;
        int corners = Dwmapi.DWMWCP_DONOTROUND;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(int));
        uint borderColor = Dwmapi.DWMWA_COLOR_NONE;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
    }

    public void Dispose() => _subclass.Dispose();

    private static nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != User32.WM_STYLECHANGING)
            return null;

        var styles = (User32.STYLESTRUCT*)lParam;
        if (wParam == User32.GWL_STYLE)
            styles->styleNew = (styles->styleNew & ~WS_FRAME) | WS_POPUP;
        else if (wParam == User32.GWL_EXSTYLE)
            styles->styleNew &= ~WS_EX_FRAME;

        // Not passed on: the WinUI window would put its frame back.
        return 0;
    }
}
