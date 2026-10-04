using Windows.Graphics;

namespace NeoShell.StartMenu;

/// <summary>Where Start goes, in physical pixels.</summary>
public static class StartMenuLayout
{
    // Effective pixels. The minimum keeps four pinned apps to a row and room for the footer.
    public const double MinWidth = 480;
    public const double MinHeight = 400;
    private const double Gap = 12;

    /// <summary>
    /// Above the taskbar, centred on the monitor or at its left like the taskbar items, at the user's size (effective
    /// pixels) as far as the monitor allows.
    /// </summary>
    public static RectInt32 Bounds(RectInt32 monitor, RectInt32 taskbar, bool centered, double width, double height, double scale)
    {
        int gap = (int)(Gap * scale);
        int maxWidth = monitor.Width - 2 * gap;
        int maxHeight = taskbar.Y - monitor.Y - 2 * gap;
        int physicalWidth = Math.Min(Math.Max((int)(width * scale), (int)(MinWidth * scale)), maxWidth);
        int physicalHeight = Math.Min(Math.Max((int)(height * scale), (int)(MinHeight * scale)), maxHeight);
        int x = centered ? monitor.X + (monitor.Width - physicalWidth) / 2 : taskbar.X + gap;
        return new RectInt32(x, taskbar.Y - physicalHeight - gap, physicalWidth, physicalHeight);
    }

    /// <summary>
    /// The size (effective pixels) after dragging a top corner by <paramref name="dx"/>, <paramref name="dy"/> physical
    /// pixels: the bottom stays on the taskbar, and a centred Start grows on both sides.
    /// </summary>
    public static (double Width, double Height) Resize(RectInt32 start, int dx, int dy, bool leftCorner, bool centered, double scale)
    {
        int grow = leftCorner ? -dx : dx;
        if (centered)
            grow *= 2;
        return (Math.Max((start.Width + grow) / scale, MinWidth), Math.Max((start.Height - dy) / scale, MinHeight));
    }
}
