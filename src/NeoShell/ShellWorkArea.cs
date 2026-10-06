using System.Collections.Concurrent;
using System.ComponentModel;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell;

/// <summary>
/// The work area (where windows maximize) of each monitor while NeoShell is the shell: the monitor less the taskbar
/// along its bottom, the widget sidebar along its right and other apps' app bars (<c>Tray.AppBars</c>). Each
/// reserves its own space here, so none undoes another's. Alongside Explorer, app bars do this instead
/// (<see cref="AppBar"/>).
/// </summary>
public static class ShellWorkArea
{
    // Written on the UI thread; read by the changes going out.
    private static readonly ConcurrentDictionary<RectInt32, Reservation> s_reserved = [];

    // Changes go out one at a time and in order, off the UI thread: setting one waits for every window (WorkArea.Set).
    // Each sets the latest reservation, so a change that went out at once (app bars) is never undone by an older one.
    private static Task s_pending = Task.CompletedTask;
    private static volatile bool s_exiting;

    /// <summary>What's reserved on a monitor changed (raised on the UI thread with the monitor's bounds).</summary>
    public static event Action<RectInt32>? Changed;

    public static void ReserveBottom(RectInt32 monitor, int height) =>
        Reserve(monitor, Reserved(monitor) with { Bottom = height });

    public static void ReserveRight(RectInt32 monitor, int width) =>
        Reserve(monitor, Reserved(monitor) with { Right = width });

    /// <summary>
    /// Reserves what other apps' app bars take: <paramref name="free"/> is the monitor less their space. Returns whether
    /// that changed anything. Set at once, without waiting for windows, as Explorer does: a bar that set its position
    /// may read the work area or maximize a window right after.
    /// </summary>
    public static bool ReserveAppBars(RectInt32 monitor, RectInt32 free) =>
        Reserve(monitor, Reserved(monitor) with { AppBarsFree = free == monitor ? null : free }, now: true);

    /// <summary>The height of the taskbar's strip and the width of the sidebar's on the monitor, 0 for none.</summary>
    public static (int Bottom, int Right) Strips(RectInt32 monitor) => (Reserved(monitor).Bottom, Reserved(monitor).Right);

    /// <summary>The work area as reserved here, which Windows may not have taken yet.</summary>
    public static RectInt32 Get(RectInt32 monitor) => Compute(monitor, Reserved(monitor));

    /// <summary>
    /// Where a window can be dragged: the work area with the sidebar's strip, which windows may cover. Windows keeps
    /// the pointer inside the work area while it moves a window, and the sidebar shouldn't fence windows off.
    /// </summary>
    public static RectInt32 DragArea(RectInt32 monitor) => Compute(monitor, Reserved(monitor) with { Right = 0 });

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
        foreach ((RectInt32 monitor, Reservation reserved) in s_reserved)
            Apply(Compute(monitor, reserved), waitForWindows: false);
    }

    private static Reservation Reserved(RectInt32 monitor) => s_reserved.GetValueOrDefault(monitor);

    private static RectInt32 Compute(RectInt32 monitor, Reservation reserved)
    {
        RectInt32 area = Compute(monitor, reserved.Bottom, reserved.Right);
        if (reserved.AppBarsFree is not { } free)
            return area;
        // App bars take their space from the edges as the taskbar and the sidebar do; what's left is both's.
        int left = Math.Max(area.X, free.X);
        int top = Math.Max(area.Y, free.Y);
        int right = Math.Min(area.X + area.Width, free.X + free.Width);
        int bottom = Math.Min(area.Y + area.Height, free.Y + free.Height);
        return new RectInt32(left, top, right - left, bottom - top);
    }

    private static bool Reserve(RectInt32 monitor, Reservation reserved, bool now = false)
    {
        // Setting it tells every window: only when it changes.
        if (s_reserved.TryGetValue(monitor, out var current) && current == reserved)
            return false;

        s_reserved[monitor] = reserved;
        if (s_exiting)
            return true;
        if (now)
        {
            Apply(Compute(monitor, reserved), waitForWindows: false);
        }
        else
        {
            s_pending = s_pending.ContinueWith(_ =>
            {
                if (!s_exiting)
                    Apply(Compute(monitor, Reserved(monitor)), waitForWindows: true);
            }, TaskScheduler.Default);
        }
        Changed?.Invoke(monitor);
        return true;
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

    /// <param name="AppBarsFree">The monitor less other apps' app bars, or null when there are none.</param>
    private readonly record struct Reservation(int Bottom, int Right, RectInt32? AppBarsFree);
}
