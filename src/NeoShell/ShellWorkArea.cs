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

    public static void ReserveBottom(RectInt32 monitor, int height) =>
        Reserve(monitor, Reserved(monitor) with { Bottom = height });

    public static void ReserveRight(RectInt32 monitor, int width) =>
        Reserve(monitor, Reserved(monitor) with { Right = width });

    /// <summary>The work area as reserved here, which Windows may not have taken yet.</summary>
    public static RectInt32 Get(RectInt32 monitor) =>
        Compute(monitor, Reserved(monitor).Bottom, Reserved(monitor).Right);

    public static RectInt32 Compute(RectInt32 monitor, int bottom, int right) =>
        new(monitor.X, monitor.Y, monitor.Width - right, monitor.Height - bottom);

    /// <summary>Waits a moment for changes still going out, so the space is given back before NeoShell exits.</summary>
    public static void Flush() => s_pending.Wait(TimeSpan.FromSeconds(2));

    private static (int Bottom, int Right) Reserved(RectInt32 monitor) => s_reserved.GetValueOrDefault(monitor);

    private static void Reserve(RectInt32 monitor, (int Bottom, int Right) reserved)
    {
        // Setting it tells every window: only when it changes.
        if (s_reserved.TryGetValue(monitor, out var current) && current == reserved)
            return;

        s_reserved[monitor] = reserved;
        RectInt32 area = Compute(monitor, reserved.Bottom, reserved.Right);
        s_pending = s_pending.ContinueWith(_ => Apply(area), TaskScheduler.Default);
    }

    private static void Apply(RectInt32 area)
    {
        try
        {
            WorkArea.Set(area);
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"Could not set the work area to {area.Width}x{area.Height} at ({area.X},{area.Y})", ex);
        }
    }
}
