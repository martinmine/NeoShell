using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Aero Peek: DWM shows one window and hides all the others, as Explorer does while a thumbnail is hovered.
/// </summary>
public static unsafe class Peek
{
    /// <summary>Peeks at <paramref name="window"/>. Ignored while a peek is on: end that one first.</summary>
    /// <param name="above">
    /// The window the peek comes from (a thumbnail popup, or the taskbar), kept above the peeked window; being excluded
    /// from peek alone doesn't keep a window there.
    /// </param>
    public static void Show(nint window, nint above) =>
        Dwmapi.DwmpActivateLivePreview(1, window, above, Dwmapi.LPT_TASKBAR, 0);

    /// <summary>Ends the peek: every window shows again.</summary>
    public static void End(nint above) =>
        Dwmapi.DwmpActivateLivePreview(0, 0, above, Dwmapi.LPT_TASKBAR, 0);

    /// <summary>Keeps the window visible while peeking, as Explorer's own taskbar, desktop and thumbnails are.</summary>
    public static void Exclude(nint hwnd)
    {
        int excluded = 1;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_EXCLUDED_FROM_PEEK, &excluded, sizeof(int));
    }
}
