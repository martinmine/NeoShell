using Windows.Graphics;

namespace NeoShell.Desktop;

/// <summary>Where the wallpaper image is drawn on a monitor, in physical pixels.</summary>
public static class WallpaperLayout
{
    /// <summary>A tiny tile image would otherwise mean an element per few pixels; above this, tiles are drawn as Fill.</summary>
    public const int MaxTiles = 4096;

    /// <summary>
    /// Returns the rectangles to draw the image into, relative to the monitor's top-left corner.
    /// One rectangle for every style except Tile. Parts outside the monitor are clipped by the window.
    /// </summary>
    public static IReadOnlyList<RectInt32> Arrange(WallpaperStyle style, SizeInt32 image, RectInt32 monitor, RectInt32 virtualScreen)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return [];

        var monitorSize = new SizeInt32(monitor.Width, monitor.Height);
        switch (style)
        {
            case WallpaperStyle.Fit:
                return [Centered(Scale(image, Math.Min(Ratio(monitor.Width, image.Width), Ratio(monitor.Height, image.Height))), monitorSize)];
            case WallpaperStyle.Stretch:
                return [new RectInt32(0, 0, monitor.Width, monitor.Height)];
            case WallpaperStyle.Center:
                return [Centered(image, monitorSize)];
            case WallpaperStyle.Tile:
                return Tile(image, monitor) ?? [Fill(image, monitorSize)];
            case WallpaperStyle.Span:
                // Fill the bounding box of all monitors, then take this monitor's part of it.
                RectInt32 spanned = Fill(image, new SizeInt32(virtualScreen.Width, virtualScreen.Height));
                return [new RectInt32(
                    spanned.X + virtualScreen.X - monitor.X,
                    spanned.Y + virtualScreen.Y - monitor.Y,
                    spanned.Width,
                    spanned.Height)];
            default:
                return [Fill(image, monitorSize)];
        }
    }

    /// <summary>The bounding box of the given rectangles: the virtual screen when given all monitors.</summary>
    public static RectInt32 Union(IEnumerable<RectInt32> rects)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (RectInt32 rect in rects)
        {
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }
        return left > right ? default : new RectInt32(left, top, right - left, bottom - top);
    }

    private static RectInt32 Fill(SizeInt32 image, SizeInt32 area) =>
        Centered(Scale(image, Math.Max(Ratio(area.Width, image.Width), Ratio(area.Height, image.Height))), area);

    private static RectInt32[]? Tile(SizeInt32 image, RectInt32 monitor)
    {
        int columns = (monitor.Width + image.Width - 1) / image.Width;
        int rows = (monitor.Height + image.Height - 1) / image.Height;
        if ((long)columns * rows > MaxTiles)
            return null;

        var tiles = new RectInt32[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
                tiles[row * columns + column] = new RectInt32(column * image.Width, row * image.Height, image.Width, image.Height);
        }
        return tiles;
    }

    private static double Ratio(int area, int image) => (double)area / image;

    private static SizeInt32 Scale(SizeInt32 size, double factor) =>
        new((int)Math.Round(size.Width * factor), (int)Math.Round(size.Height * factor));

    private static RectInt32 Centered(SizeInt32 size, SizeInt32 area) =>
        new((int)Math.Floor((area.Width - size.Width) / 2.0), (int)Math.Floor((area.Height - size.Height) / 2.0), size.Width, size.Height);
}
