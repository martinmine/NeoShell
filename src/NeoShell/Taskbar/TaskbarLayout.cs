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

    /// <summary>The monitors that get a taskbar: all of them, or only the primary one.</summary>
    public static IReadOnlyList<DisplayMonitor> MonitorsWithTaskbar(IReadOnlyList<DisplayMonitor> monitors, bool showOnAllDisplays) =>
        showOnAllDisplays ? monitors : [.. monitors.Where(monitor => monitor.IsPrimary)];
}
