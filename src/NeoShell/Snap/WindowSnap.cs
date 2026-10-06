using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>Where a window is snapped: a half or a quarter of its monitor's work area, the whole of it, or nowhere.</summary>
public enum SnapPosition { None, Maximized, Minimized, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>The arrow pressed with the Windows key.</summary>
public enum SnapKey { Left, Right, Up, Down }

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
    public static SnapPosition AtPointer(PointInt32 pointer, RectInt32 dragArea, uint dpi)
    {
        // A couple of pixels' slack catches the pointer on an edge shared with another monitor too, which it crosses.
        int edge = Math.Max(2, (int)Math.Round(2 * dpi / 96.0));
        int corner = Math.Min(dragArea.Width, dragArea.Height) / 8;
        bool left = pointer.X < dragArea.X + edge;
        bool right = pointer.X >= dragArea.X + dragArea.Width - edge;
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
    /// What Win+arrow does to a window in this position, as in Windows 11: left and right snap to a half, or across
    /// to the other quarter, and back to its own size from the other half; up goes from a half to its top quarter,
    /// from a bottom quarter to the half, and maximizes a window that isn't snapped; down undoes those, and minimizes
    /// what's left.
    /// </summary>
    public static SnapPosition AfterKey(SnapPosition current, SnapKey key) => key switch
    {
        SnapKey.Left => current switch
        {
            SnapPosition.Right => SnapPosition.None,
            SnapPosition.TopRight => SnapPosition.TopLeft,
            SnapPosition.BottomRight => SnapPosition.BottomLeft,
            SnapPosition.TopLeft or SnapPosition.BottomLeft => current,
            _ => SnapPosition.Left,
        },
        SnapKey.Right => current switch
        {
            SnapPosition.Left => SnapPosition.None,
            SnapPosition.TopLeft => SnapPosition.TopRight,
            SnapPosition.BottomLeft => SnapPosition.BottomRight,
            SnapPosition.TopRight or SnapPosition.BottomRight => current,
            _ => SnapPosition.Right,
        },
        SnapKey.Up => current switch
        {
            SnapPosition.Left => SnapPosition.TopLeft,
            SnapPosition.Right => SnapPosition.TopRight,
            SnapPosition.BottomLeft => SnapPosition.Left,
            SnapPosition.BottomRight => SnapPosition.Right,
            SnapPosition.TopLeft or SnapPosition.TopRight => current,
            _ => SnapPosition.Maximized,
        },
        _ => current switch
        {
            SnapPosition.Maximized => SnapPosition.None,
            SnapPosition.Left => SnapPosition.BottomLeft,
            SnapPosition.Right => SnapPosition.BottomRight,
            SnapPosition.TopLeft => SnapPosition.Left,
            SnapPosition.TopRight => SnapPosition.Right,
            _ => SnapPosition.Minimized,
        },
    };

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
