using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Aero Peek: DWM shows one window and hides all the others, as Explorer does while a thumbnail is hovered.
/// </summary>
public static unsafe class Peek
{
    /// <summary>Peeks at <paramref name="window"/>. Ignored while a peek is on: end that one first.</summary>
    /// <param name="taskbar">The taskbar the peek comes from, as Explorer passes its own.</param>
    public static void Show(nint window, nint taskbar) =>
        Dwmapi.DwmpActivateLivePreview(1, window, taskbar, Dwmapi.LPT_TASKBAR, 0);

    /// <summary>Ends the peek: every window shows again.</summary>
    public static void End(nint taskbar) =>
        Dwmapi.DwmpActivateLivePreview(0, 0, taskbar, Dwmapi.LPT_TASKBAR, 0);

    /// <summary>Keeps the window visible while peeking, as Explorer's own taskbar, desktop and thumbnails are.</summary>
    public static void Exclude(nint hwnd)
    {
        int excluded = 1;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_EXCLUDED_FROM_PEEK, &excluded, sizeof(int));
    }
}
