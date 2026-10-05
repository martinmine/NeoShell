using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

internal static class WindowRegion
{
    /// <summary>Shows only the window's top <paramref name="height"/> pixels, or all of it when null.</summary>
    public static void SetVisibleHeight(nint hwnd, int width, int? height)
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
}
