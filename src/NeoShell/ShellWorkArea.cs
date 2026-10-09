using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell;

/// <summary>
/// The work area (where windows maximize) of each monitor while NeoShell is the shell: the monitor less the taskbar
/// along its bottom and other apps' app bars (<c>Tray.AppBars</c>). Each reserves its own space here, so neither
/// undoes the other's. The widget sidebar takes none: windows maximize over it. Alongside Explorer, app bars do this instead
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

    // UI thread only: the monitors whose reservation changed since changes last went out.
    private static readonly HashSet<RectInt32> s_unsent = [];

    /// <summary>What's reserved on a monitor changed (raised on the UI thread with the monitor's bounds).</summary>
    public static event Action<RectInt32>? Changed;

    public static void ReserveBottom(RectInt32 monitor, int height) =>
        Reserve(monitor, Reserved(monitor) with { Bottom = height });

    /// <summary>
    /// Reserves what other apps' app bars take: <paramref name="free"/> is the monitor less their space. Returns whether
    /// that changed anything. Set at once, without waiting for windows, as Explorer does: a bar that set its position
    /// may read the work area or maximize a window right after.
    /// </summary>
    public static bool ReserveAppBars(RectInt32 monitor, RectInt32 free) =>
        Reserve(monitor, Reserved(monitor) with { AppBarsFree = free == monitor ? null : free }, now: true);

    /// <summary>The height of the taskbar's strip on the monitor, 0 for none.</summary>
    public static int TaskbarHeight(RectInt32 monitor) => Reserved(monitor).Bottom;

    /// <summary>The work area as reserved here, which Windows may not have taken yet.</summary>
    public static RectInt32 Get(RectInt32 monitor) => Compute(monitor, Reserved(monitor));

    /// <summary>
    /// The monitors with their work areas as reserved here, for what's placed in them (Snap). Windows' own can be the
    /// whole monitor for seconds: it gives each monitor its whole screen when the displays change (as they do right
    /// after signing in), until NeoShell hears of the change and the reservations go out again (<see cref="SendAgain"/>).
    /// </summary>
    public static IReadOnlyList<DisplayMonitor> Monitors() =>
        [.. DisplayMonitor.GetAll().Select(monitor => monitor with { WorkArea = Get(monitor.Bounds) })];

    public static RectInt32 Compute(RectInt32 monitor, int bottom) =>
        new(monitor.X, monitor.Y, monitor.Width, monitor.Height - bottom);

    /// <summary>
    /// Every monitor's reservation goes out again where Windows' work area differs from it. Windows gives each monitor
    /// its whole screen as work area when the displays change (monitors come or go, a resolution changes), though
    /// nothing reserved there changed; Explorer works out every work area again then (<c>CTray::RecomputeAllWorkareas</c>).
    /// </summary>
    public static void SendAgain()
    {
        foreach (RectInt32 monitor in s_reserved.Keys)
            Unsent(monitor);
    }

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
        foreach (RectInt32 monitor in s_reserved.Keys)
            Apply(monitor, waitForWindows: false);
    }

    private static Reservation Reserved(RectInt32 monitor) => s_reserved.GetValueOrDefault(monitor);

    private static RectInt32 Compute(RectInt32 monitor, Reservation reserved)
    {
        RectInt32 area = Compute(monitor, reserved.Bottom);
        if (reserved.AppBarsFree is not { } free)
            return area;
        // App bars take their space from the edges as the taskbar does; what's left is both's.
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
            Apply(monitor, waitForWindows: false);
        else
            Unsent(monitor);
        Changed?.Invoke(monitor);
        return true;
    }

    // What's reserved together goes out as one change once the UI thread is done, as Explorer defers work area changes:
    // a taskbar made again gives its space back and takes it again, and each change goes out resizing the maximized
    // windows (WorkArea.Set), which would grow under the taskbar and shrink back.
    private static void Unsent(RectInt32 monitor)
    {
        if (s_unsent.Count == 0)
            DispatcherQueue.GetForCurrentThread().Post(SendUnsent);
        s_unsent.Add(monitor);
    }

    private static void SendUnsent()
    {
        foreach (RectInt32 monitor in s_unsent)
        {
            s_pending = s_pending.ContinueWith(_ =>
            {
                if (!s_exiting)
                    Apply(monitor, waitForWindows: true);
            }, TaskScheduler.Default);
        }
        s_unsent.Clear();
    }

    /// <summary>Sets the monitor's work area to what's reserved there, unless Windows has that already.</summary>
    private static void Apply(RectInt32 monitor, bool waitForWindows)
    {
        // Compared with Windows' work area as it is now, as Explorer does (CTray::RecomputeWorkArea), not with what went
        // out last: Windows may have given the monitor its whole screen back since. A monitor that's gone has none.
        RectInt32 area = Compute(monitor, Reserved(monitor));
        if (WorkArea.Get(monitor) is not { } current || current == area)
            return;
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
    private readonly record struct Reservation(int Bottom, RectInt32? AppBarsFree);
}
