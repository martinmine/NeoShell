using System.ComponentModel;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell;

/// <summary>
/// The work area (where windows maximize) of each monitor while NeoShell is the shell: the monitor less the taskbar
/// along its bottom and the widget sidebar along its right. Each reserves its own edge here, so neither undoes the
/// other's. Alongside Explorer, app bars do this instead (<see cref="AppBar"/>).
/// </summary>
public static class ShellWorkArea
{
    private static readonly Dictionary<RectInt32, (int Bottom, int Right)> s_reserved = [];

    // Changes go out one at a time and in order, off the UI thread: setting one waits for every window (WorkArea.Set).
    private static Task s_pending = Task.CompletedTask;
    private static volatile bool s_exiting;

    public static void ReserveBottom(RectInt32 monitor, int height) =>
        Reserve(monitor, Reserved(monitor) with { Bottom = height });

    public static void ReserveRight(RectInt32 monitor, int width) =>
        Reserve(monitor, Reserved(monitor) with { Right = width });

    /// <summary>The work area as reserved here, which Windows may not have taken yet.</summary>
    public static RectInt32 Get(RectInt32 monitor) =>
        Compute(monitor, Reserved(monitor).Bottom, Reserved(monitor).Right);

    /// <summary>
    /// Where a window can be dragged: the work area with the sidebar's strip, which windows may cover. Windows keeps
    /// the pointer inside the work area while it moves a window, and the sidebar shouldn't fence windows off.
    /// </summary>
    public static RectInt32 DragArea(RectInt32 monitor) => Compute(monitor, Reserved(monitor).Bottom, 0);

    public static RectInt32 Compute(RectInt32 monitor, int bottom, int right) =>
        new(monitor.X, monitor.Y, monitor.Width - right, monitor.Height - bottom);

    /// <summary>
    /// NeoShell is exiting: from now on changes are only noted, for <see cref="Restore"/>. One going out would wait for
    /// NeoShell's own windows, which are closing and no longer answer.
    /// </summary>
    public static void BeginExit() => s_exiting = true;

    /// <summary>
    /// Gives the space back as NeoShell exits, once its windows have let go of theirs, without waiting for windows.
    /// </summary>
    public static void Restore()
    {
        foreach ((RectInt32 monitor, (int bottom, int right)) in s_reserved)
            Apply(Compute(monitor, bottom, right), waitForWindows: false);
    }

    private static (int Bottom, int Right) Reserved(RectInt32 monitor) => s_reserved.GetValueOrDefault(monitor);

    private static void Reserve(RectInt32 monitor, (int Bottom, int Right) reserved)
    {
        // Setting it tells every window: only when it changes.
        if (s_reserved.TryGetValue(monitor, out var current) && current == reserved)
            return;

        s_reserved[monitor] = reserved;
        if (s_exiting)
            return;
        RectInt32 area = Compute(monitor, reserved.Bottom, reserved.Right);
        s_pending = s_pending.ContinueWith(_ =>
        {
            if (!s_exiting)
                Apply(area, waitForWindows: true);
        }, TaskScheduler.Default);
    }

    private static void Apply(RectInt32 area, bool waitForWindows)
    {
        try
        {
            WorkArea.Set(area, waitForWindows);
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"Could not set the work area to {area.Width}x{area.Height} at ({area.X},{area.Y})", ex);
        }
    }
}
