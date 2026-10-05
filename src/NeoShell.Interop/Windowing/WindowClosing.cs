using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public static class WindowClosing
{
    /// <summary>
    /// Keeps moves and resizes of a WinUI window that's about to close from reaching WinUI. While closing, WinUI
    /// destroys the window's content first and then re-applies the window's styles; when that moves the client area,
    /// WinUI's own move handler repositions the content it has already destroyed (an access violation in
    /// <c>CWindowChrome::UpdateBridgeWindowSizePosition</c>). The guard goes with the window.
    /// </summary>
    public static void IgnoreMoves(nint hwnd) =>
        _ = new WindowSubclass(hwnd, (message, _, _) => message is User32.WM_MOVE or User32.WM_SIZE ? 0 : null);
}
