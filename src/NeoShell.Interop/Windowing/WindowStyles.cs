using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

[Flags]
public enum ExtendedWindowStyles : uint
{
    None = 0,
    /// <summary>Not shown in Alt+Tab or on the taskbar.</summary>
    ToolWindow = 0x0000_0080,
    /// <summary>Clicking the window doesn't activate it, so it doesn't take focus from the app the user is in.</summary>
    NoActivate = 0x0800_0000,
}

public static class WindowStyles
{
    public static ExtendedWindowStyles GetExtended(nint hwnd) =>
        (ExtendedWindowStyles)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);

    public static void AddExtended(nint hwnd, ExtendedWindowStyles styles) =>
        SetExtended(hwnd, GetExtended(hwnd) | styles);

    public static void RemoveExtended(nint hwnd, ExtendedWindowStyles styles) =>
        SetExtended(hwnd, GetExtended(hwnd) & ~styles);

    private static void SetExtended(nint hwnd, ExtendedWindowStyles styles)
    {
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, (nint)styles);
        // Cached frame and style data are only refreshed by SWP_FRAMECHANGED.
        User32.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);
    }
}
