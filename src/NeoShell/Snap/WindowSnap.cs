using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// Where a window is snapped: a half or a quarter of its monitor's work area, the whole of it, or nowhere; or
/// <see cref="Tall"/>, stretched to the work area's height at its own width (Win+Shift+Up).
/// </summary>
public enum SnapPosition { None, Maximized, Minimized, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight, Tall }

/// <summary>The arrow pressed with the Windows key, and with Shift too.</summary>
public enum SnapKey { Left, Right, Up, Down, ShiftLeft, ShiftRight, ShiftUp, ShiftDown }

/// <summary>Where a Win+arrow key takes a window: a position on its own monitor, or on the next one either way (-1 or 1).</summary>
public readonly record struct SnapMove(SnapPosition Position, int Monitor = 0);

/// <summary>The invisible borders around what's drawn of a window, in pixels.</summary>
public readonly record struct WindowBorders(int Left, int Top, int Right, int Bottom);

/// <summary>Windows 11's Snap rules: which zone a dragged window snaps to, and what Win+arrow keys do.</summary>
public static class WindowSnap
{
    /// <summary>The zone of a half or quarter, as a fraction of the work area; null for the other positions.</summary>
    public static SnapZone? Zone(SnapPosition position) => position switch
    {
        SnapPosition.Left => new(0, 0, 0.5, 1),
        SnapPosition.Right => new(0.5, 0, 0.5, 1),
        SnapPosition.TopLeft => new(0, 0, 0.5, 0.5),
        SnapPosition.TopRight => new(0.5, 0, 0.5, 0.5),
        SnapPosition.BottomLeft => new(0, 0.5, 0.5, 0.5),
        SnapPosition.BottomRight => new(0.5, 0.5, 0.5, 0.5),
        _ => null,
    };

    /// <summary>
    /// Where a window dragged with the pointer here snaps, as in Windows: pushed against the left or right edge, to
    /// that half, or a quarter near a corner; against the top edge, it fills the screen, or a quarter near a corner.
    /// Elsewhere nowhere. The edges are those of the area the pointer is kept in while a window is dragged: the work
    /// area, so the taskbar's edge rather than the screen's, but with the widget sidebar's strip.
    /// </summary>
    /// <param name="pointer">The pointer, in screen pixels.</param>
    /// <param name="dragArea">Where the pointer can go on the monitor it's on while the window is dragged.</param>
    /// <param name="nearEdge">
    /// "Let me snap it without dragging all the way to the screen edge" (<see cref="SnapSettings.NearEdge"/>): the
    /// sides then count from 63 effective pixels away and the top from 7, as measured on Explorer; otherwise only the
    /// edge itself.
    /// </param>
    public static SnapPosition AtPointer(PointInt32 pointer, RectInt32 dragArea, uint dpi, bool nearEdge = false)
    {
        // A couple of pixels' slack catches the pointer on an edge shared with another monitor too, which it crosses.
        int side = Math.Max(2, (int)Math.Round((nearEdge ? 63 : 2) * dpi / 96.0));
        int edge = nearEdge ? (int)Math.Round(7 * dpi / 96.0) : 1;
        int corner = Math.Min(dragArea.Width, dragArea.Height) / 8;
        bool left = pointer.X < dragArea.X + side;
        bool right = pointer.X >= dragArea.X + dragArea.Width - side;
        bool top = pointer.Y < dragArea.Y + edge;
        bool nearTop = pointer.Y < dragArea.Y + corner;
        bool nearBottom = pointer.Y >= dragArea.Y + dragArea.Height - corner;

        if (left)
            return nearTop ? SnapPosition.TopLeft : nearBottom ? SnapPosition.BottomLeft : SnapPosition.Left;
        if (right)
            return nearTop ? SnapPosition.TopRight : nearBottom ? SnapPosition.BottomRight : SnapPosition.Right;
        if (top)
        {
            return pointer.X < dragArea.X + corner ? SnapPosition.TopLeft
                : pointer.X >= dragArea.X + dragArea.Width - corner ? SnapPosition.TopRight
                : SnapPosition.Maximized;
        }
        return SnapPosition.None;
    }

    /// <summary>
    /// What Win+arrow does to a window in this position, as in Windows 11: left and right snap to a half, or across to
    /// the other quarter, back to its own size from the other half (or from maximized), and from the far half or
    /// quarter on to the next monitor's near one; up goes from a half to its top quarter, from a bottom quarter to the
    /// half, and maximizes the rest; down undoes those, and minimizes what's left. With Shift, up stretches the window
    /// to the work area's height (a quarter to its half), down gives it its own size back, and left and right take it
    /// to the next monitor in the same position (stretched, it's maximized there, as with Explorer).
    /// </summary>
    public static SnapMove AfterKey(SnapPosition current, SnapKey key) => key switch
    {
        SnapKey.Left => current switch
        {
            SnapPosition.Right or SnapPosition.Maximized => new(SnapPosition.None),
            SnapPosition.TopRight => new(SnapPosition.TopLeft),
            SnapPosition.BottomRight => new(SnapPosition.BottomLeft),
            SnapPosition.Left => new(SnapPosition.Right, -1),
            SnapPosition.TopLeft => new(SnapPosition.TopRight, -1),
            SnapPosition.BottomLeft => new(SnapPosition.BottomRight, -1),
            _ => new(SnapPosition.Left),
        },
        SnapKey.Right => current switch
        {
            SnapPosition.Left or SnapPosition.Maximized => new(SnapPosition.None),
            SnapPosition.TopLeft => new(SnapPosition.TopRight),
            SnapPosition.BottomLeft => new(SnapPosition.BottomRight),
            SnapPosition.Right => new(SnapPosition.Left, 1),
            SnapPosition.TopRight => new(SnapPosition.TopLeft, 1),
            SnapPosition.BottomRight => new(SnapPosition.BottomLeft, 1),
            _ => new(SnapPosition.Right),
        },
        SnapKey.Up => current switch
        {
            SnapPosition.Left => new(SnapPosition.TopLeft),
            SnapPosition.Right => new(SnapPosition.TopRight),
            SnapPosition.BottomLeft => new(SnapPosition.Left),
            SnapPosition.BottomRight => new(SnapPosition.Right),
            _ => new(SnapPosition.Maximized),
        },
        SnapKey.Down => current switch
        {
            SnapPosition.Maximized or SnapPosition.Tall => new(SnapPosition.None),
            SnapPosition.Left => new(SnapPosition.BottomLeft),
            SnapPosition.Right => new(SnapPosition.BottomRight),
            SnapPosition.TopLeft => new(SnapPosition.Left),
            SnapPosition.TopRight => new(SnapPosition.Right),
            _ => new(SnapPosition.Minimized),
        },
        SnapKey.ShiftUp => current switch
        {
            SnapPosition.None => new(SnapPosition.Tall),
            SnapPosition.TopLeft or SnapPosition.BottomLeft => new(SnapPosition.Left),
            SnapPosition.TopRight or SnapPosition.BottomRight => new(SnapPosition.Right),
            _ => new(current),
        },
        SnapKey.ShiftDown => new(current == SnapPosition.Minimized ? current : SnapPosition.None),
        _ => new(current == SnapPosition.Tall ? SnapPosition.Maximized : current, key == SnapKey.ShiftLeft ? -1 : 1),
    };

    /// <summary>The monitor <paramref name="step"/> on from <paramref name="current"/> of <paramref name="count"/>, round from the end to the start.</summary>
    public static int Neighbour(int count, int current, int step) => ((current + step) % count + count) % count;

    /// <summary>A window stretched to the work area's height (Win+Shift+Up): what's drawn of it, where it is across.</summary>
    public static RectInt32 Tall(RectInt32 visible, RectInt32 workArea) =>
        new(visible.X, workArea.Y, visible.Width, workArea.Height);

    /// <summary>
    /// Where Windows puts a window moved to another monitor with Win+Shift+arrow (win32k's
    /// <c>AdvancedWindowPos::xxxTransformRectToMonitor</c>): its size scaled for the other monitor's DPI; what's drawn
    /// of it at the same share of the way across and down the monitor (rounded as Windows rounds); then moved into the
    /// work area, and cut to it where a window that can be resized is still too big.
    /// </summary>
    /// <param name="bounds">The window's bounds, invisible borders included.</param>
    /// <param name="borders">Its invisible borders at the other monitor's DPI.</param>
    public static RectInt32 OnMonitor(RectInt32 bounds, WindowBorders borders, RectInt32 from, RectInt32 to, RectInt32 toWorkArea,
        uint fromDpi, uint toDpi, bool resizable)
    {
        int width = fromDpi == toDpi ? bounds.Width : MulDiv(bounds.Width, toDpi, fromDpi);
        int height = fromDpi == toDpi ? bounds.Height : MulDiv(bounds.Height, toDpi, fromDpi);
        int left = to.X + Across(bounds.X + borders.Left - from.X, from.Width, to.Width);
        int top = to.Y + Across(bounds.Y + borders.Top - from.Y, from.Height, to.Height);
        int right = left + width - borders.Left - borders.Right;
        int bottom = top + height - borders.Top - borders.Bottom;

        // Windows' FitRectToWorkArea: back in from the right and the bottom, then from the left and the top.
        int workRight = toWorkArea.X + toWorkArea.Width, workBottom = toWorkArea.Y + toWorkArea.Height;
        if (right > workRight)
            (left, right) = (left - (right - workRight), workRight);
        if (left < toWorkArea.X)
            (left, right) = (toWorkArea.X, right + toWorkArea.X - left);
        if (bottom > workBottom)
            (top, bottom) = (top - (bottom - workBottom), workBottom);
        if (top < toWorkArea.Y)
            (top, bottom) = (toWorkArea.Y, bottom + toWorkArea.Y - top);
        if (resizable)
        {
            right = Math.Min(right, workRight);
            bottom = Math.Min(bottom, workBottom);
        }
        return new RectInt32(left - borders.Left, top - borders.Top,
            right - left + borders.Left + borders.Right, bottom - top + borders.Top + borders.Bottom);

        // An offset into a monitor this long, at the same share of one that long: win32k's sum, C's division.
        static int Across(int offset, int from, int to) => offset + (from / 2 + offset * (to - from)) / from;
        static int MulDiv(int value, uint by, uint over) => (int)Math.Round(value * (double)by / over, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Where a snapped window dragged away goes when let go: its own size again, under the pointer at the same share
    /// of its width as where it was grabbed, at the height it was dropped.
    /// </summary>
    /// <param name="dropped">The window's bounds where it was let go.</param>
    /// <param name="restore">Its bounds before it was snapped.</param>
    /// <param name="grab">Where along its width it was grabbed, from 0 (left) to 1 (right).</param>
    public static RectInt32 Unsnapped(RectInt32 dropped, RectInt32 restore, int pointerX, double grab) =>
        new(pointerX - (int)Math.Round(grab * restore.Width), dropped.Y, restore.Width, restore.Height);
}
