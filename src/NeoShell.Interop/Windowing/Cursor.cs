using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public static class Cursor
{
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
