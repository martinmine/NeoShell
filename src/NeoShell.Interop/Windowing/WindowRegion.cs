using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>Which part of a window shows (and takes the mouse); the rest is as if the window weren't there.</summary>
public static class WindowRegion
{
    /// <summary>Shows only the window's top <paramref name="height"/> pixels, or all of it when null.</summary>
    internal static void SetVisibleHeight(nint hwnd, int width, int? height)
    {
        // DWM draws a window with an empty region whole, as if it had none (a frame of Start behind a see-through
        // taskbar), so showing nothing is a pixel outside the window. The system owns a region once it's set.
        nint region = height switch
        {
            null => 0,
            <= 0 => Gdi32.CreateRectRgn(-1, -1, 0, 0),
            _ => Gdi32.CreateRectRgn(0, 0, width, height.Value),
        };
        User32.SetWindowRgn(hwnd, region, true);
    }

    /// <summary>
    /// Shows only <paramref name="rects"/> (pixels from the window's top-left corner) with their corners rounded, or
    /// all of the window when null. Its backdrop is cut to them too.
    /// </summary>
    public static void SetRoundedRects(nint hwnd, IReadOnlyList<RectInt32>? rects, int cornerRadius)
    {
        if (rects is null)
        {
            User32.SetWindowRgn(hwnd, 0, true);
            return;
        }

        // Nothing to show is a pixel outside the window, as above.
        nint region = Gdi32.CreateRectRgn(-1, -1, 0, 0);
        foreach (RectInt32 rect in rects)
        {
            // The ellipse's size is the corner's diameter; the right and bottom edges are exclusive.
            nint part = Gdi32.CreateRoundRectRgn(rect.X, rect.Y, rect.X + rect.Width + 1, rect.Y + rect.Height + 1, 2 * cornerRadius, 2 * cornerRadius);
            Gdi32.CombineRgn(region, region, part, Gdi32.RGN_OR);
            Gdi32.DeleteObject(part);
        }
        User32.SetWindowRgn(hwnd, region, true);
    }
}
