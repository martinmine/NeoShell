using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public static class Cursor
{
    /// <summary>The mouse position in screen pixels.</summary>
    public static PointInt32 Position() =>
        User32.GetCursorPos(out User32.POINT point) ? new PointInt32(point.x, point.y) : default;
}
