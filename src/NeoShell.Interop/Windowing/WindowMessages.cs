using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public static class WindowMessages
{
    /// <summary>Returns the system-wide message ID for <paramref name="name"/>; every process gets the same ID.</summary>
    public static uint Register(string name) => User32.RegisterWindowMessage(name);

    public static bool Post(nint hwnd, uint message, nint wParam = 0, nint lParam = 0) =>
        User32.PostMessage(hwnd, message, wParam, lParam);
}
