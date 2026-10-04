using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Taskbar;

/// <summary>Where taskbars go, in physical pixels.</summary>
public static class TaskbarLayout
{
    /// <summary>Height in effective pixels, as the Windows 11 taskbar.</summary>
    public const int Height = 48;

    public static int PhysicalHeight(uint dpi) => (int)Math.Round(Height * dpi / 96.0);

    /// <summary>The strip along the bottom of the monitor.</summary>
    public static RectInt32 Bounds(RectInt32 monitor, uint dpi)
    {
        int height = PhysicalHeight(dpi);
        return new RectInt32(monitor.X, monitor.Y + monitor.Height - height, monitor.Width, height);
    }

    /// <summary>What's left of the monitor above the taskbar, for windows to maximize into.</summary>
    public static RectInt32 WorkArea(RectInt32 monitor, RectInt32 taskbar) =>
        new(monitor.X, monitor.Y, monitor.Width, taskbar.Y - monitor.Y);

    /// <summary>Pixels of an auto-hidden taskbar left on screen, for the pointer to find.</summary>
    public const int AutoHideVisiblePixels = 2;

    /// <summary>Where an auto-hidden taskbar goes: slid down until only a sliver of its top edge shows.</summary>
    public static RectInt32 HiddenBounds(RectInt32 shown) =>
        shown with { Y = shown.Y + shown.Height - AutoHideVisiblePixels };

    /// <summary>
    /// Whether a window fills its monitor, like a game, a video or a browser after F11; the taskbar then makes way.
    /// A maximized window stops at the taskbar, so it doesn't count.
    /// </summary>
    public static bool IsFullScreen(RectInt32 window, RectInt32 monitor) =>
        window.X <= monitor.X
        && window.Y <= monitor.Y
        && window.X + window.Width >= monitor.X + monitor.Width
        && window.Y + window.Height >= monitor.Y + monitor.Height;

    /// <summary>The monitors that get a taskbar: all of them, or only the primary one.</summary>
    public static IReadOnlyList<DisplayMonitor> MonitorsWithTaskbar(IReadOnlyList<DisplayMonitor> monitors, bool showOnAllDisplays) =>
        showOnAllDisplays ? monitors : [.. monitors.Where(monitor => monitor.IsPrimary)];
}
