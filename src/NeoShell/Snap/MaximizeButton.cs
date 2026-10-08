using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// Where the maximize button of a window with its own title bar is, found by asking the window what's at each point (its
/// <c>WM_NCHITTEST</c> answer). Windows' own caption buttons are found from DWM instead
/// (<c>TopLevelWindows.CaptionMaximizeButton</c>).
/// </summary>
public static class MaximizeButton
{
    /// <summary>The button's extent around a point on it, in screen pixels.</summary>
    /// <param name="isButton">Whether a screen point is on the button.</param>
    public static RectInt32 Around(PointInt32 point, Func<PointInt32, bool> isButton, int limit = 120)
    {
        int left = point.X, right = point.X, top = point.Y, bottom = point.Y;
        while (point.X - left < limit && isButton(new PointInt32(left - 1, point.Y)))
            left--;
        while (right - point.X < limit && isButton(new PointInt32(right + 1, point.Y)))
            right++;
        while (point.Y - top < limit && isButton(new PointInt32(point.X, top - 1)))
            top--;
        while (bottom - point.Y < limit && isButton(new PointInt32(point.X, bottom + 1)))
            bottom++;
        return new RectInt32(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// The button of a window whose drawn part is <paramref name="visible"/>: looked for along its title bar from the
    /// right; where none answers, where Windows' own caption puts it (the second of three 46 by 30 effective pixel
    /// buttons at the top right).
    /// </summary>
    public static RectInt32 Find(RectInt32 visible, uint dpi, Func<PointInt32, bool> isButton)
    {
        double scale = dpi / 96.0;
        int y = visible.Y + (int)Math.Round(15 * scale);
        int step = Math.Max(1, (int)Math.Round(4 * scale));
        for (int x = visible.X + visible.Width - 1; x > visible.X + visible.Width / 2; x -= step)
        {
            if (isButton(new PointInt32(x, y)))
                return Around(new PointInt32(x, y), isButton);
        }
        int width = (int)Math.Round(46 * scale);
        return new RectInt32(visible.X + visible.Width - 2 * width, visible.Y, width, (int)Math.Round(30 * scale));
    }
}
