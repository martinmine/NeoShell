using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public static class Cursor
{
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;

    /// <summary>Whether a mouse button (left or right) is held down right now, whichever window has the mouse.</summary>
    public static bool IsButtonDown() => ((User32.GetAsyncKeyState(VK_LBUTTON) | User32.GetAsyncKeyState(VK_RBUTTON)) & 0x8000) != 0;

    /// <summary>The mouse position in screen pixels.</summary>
    public static PointInt32 Position() =>
        User32.GetCursorPos(out User32.POINT point) ? new PointInt32(point.x, point.y) : default;

    /// <summary>The rectangle the pointer is kept in (the whole screen when it's free), in screen pixels.</summary>
    public static RectInt32 Clip
    {
        get => User32.GetClipCursor(out User32.RECT rect) ? rect.ToRectInt32() : default;
        set => User32.ClipCursor(new User32.RECT { left = value.X, top = value.Y, right = value.X + value.Width, bottom = value.Y + value.Height });
    }
}
