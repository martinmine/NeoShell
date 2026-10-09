using Windows.Graphics;
using Windows.UI;

namespace NeoShell.StartMenu;

/// <summary>Where Start goes, in physical pixels, and the colour of its folders' panel.</summary>
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

    /// <summary>
    /// The colour of an open folder's panel. Explorer's is in-app acrylic (StartMenu.dll's <c>FolderModal</c> template:
    /// <c>AcrylicInAppFillColorDefaultBrush</c>), so it shows Start's colour behind it at the brush's luminosity: measured
    /// on Explorer's panels (2026-10-09), luminance 43 on a dark Start and 243 on a light one, and with the accent colour
    /// 47 on a dark Start and 237 on a light one (its hue kept: #14325D on #1E4277, #DEF1F6 on #B5E2EC).
    /// <paramref name="accent"/> is Start's tint when Windows shows the accent colour on Start (its palette's second
    /// dark shade); Start's acrylic greys it, so the panel is it at that luminosity two fifths of the way to grey
    /// (#00337C gives #14335E; Explorer's #14325D).
    /// </summary>
    public static Color FolderPanelColor(Color? accent, bool light)
    {
        if (accent is { } color)
        {
            double luminance = light ? 237 : 47;
            Color shade = WithLuminance(color, luminance);
            return Scale(shade, c => c + (luminance - c) * 0.4);
        }
        return WithLuminance(light ? Color.FromArgb(255, 0xF3, 0xF3, 0xF3) : Color.FromArgb(255, 0x20, 0x20, 0x20), light ? 243 : 43);
    }

    // The colour with its hue kept: darker by scaling, lighter by mixing with white.
    private static Color WithLuminance(Color color, double luminance)
    {
        double now = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        if (now <= 0)
            return Color.FromArgb(255, (byte)luminance, (byte)luminance, (byte)luminance);
        if (luminance <= now)
            return Scale(color, c => c * luminance / now);
        double toWhite = (luminance - now) / (255 - now);
        return Scale(color, c => c + (255 - c) * toWhite);
    }

    private static Color Scale(Color color, Func<double, double> channel) =>
        Color.FromArgb(255, (byte)Math.Round(channel(color.R)), (byte)Math.Round(channel(color.G)), (byte)Math.Round(channel(color.B)));
}
