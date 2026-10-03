using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public static class KeyboardState
{
    private const int VK_SHIFT = 0x10;

    /// <summary>
    /// Whether Shift is held right now. The taskbar never has keyboard focus, so its thread's own key state
    /// (what WinUI reports) doesn't see the key.
    /// </summary>
    public static bool IsShiftDown() => (User32.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
}
