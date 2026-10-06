using NeoShell.Interop.Tray;
using Windows.Graphics;

namespace NeoShell.Tray;

/// <summary>
/// An app bar: its window, the edge it's docked to and its rectangle. As in Explorer, a rectangle may come out
/// empty or crossed (left past right) when the space is used up.
/// </summary>
public readonly record struct AppBarPlace(nint Window, AppBarEdge Edge, RectInt32 Rect);

/// <summary>
/// How Explorer fits app bars together (explorer.exe 26200: <c>CTray::_AppBarQueryPos</c>, <c>_AppBarSubtractRect(s)</c>,
/// <c>_AppBarOutsideOf</c>, <c>RecomputeWorkArea</c>). Monitors are given by their bounds, the primary one first.
/// </summary>
public static class AppBarLayout
{
    /// <summary>
    /// <c>ABM_QUERYPOS</c>: the rectangle a bar asking for <paramref name="proposed"/> on <paramref name="edge"/> gets.
    /// It stays clear of the taskbar on that monitor, and of the other bars there that come first: top and bottom
    /// bars always; bars on the edge asked for that are already outside it (or all of them when it moves to that
    /// edge). It isn't moved onto the monitor: Explorer leaves a rectangle that hangs off the screen as it is.
    /// </summary>
    /// <param name="bar">The asking bar as it is now: <see cref="AppBarEdge.None"/> until it has been placed.</param>
    /// <param name="taskbars">Where the taskbars keep their space (each along the bottom of its monitor).</param>
    /// <param name="others">The other bars, NeoShell's widget sidebar among them as a bar on the right.</param>
    public static RectInt32 QueryPos(
        AppBarPlace bar, AppBarEdge edge, RectInt32 proposed, IReadOnlyList<RectInt32> monitors,
        IEnumerable<RectInt32> taskbars, IEnumerable<AppBarPlace> others)
    {
        // MONITOR_DEFAULTTOPRIMARY.
        int monitor = MonitorIndex(monitors, proposed) ?? 0;
        RectInt32 rect = proposed;
        foreach (RectInt32 taskbar in taskbars)
        {
            if (MonitorIndex(monitors, taskbar) == monitor)
                rect = Subtract(rect, AppBarEdge.Bottom, taskbar);
        }
        foreach (AppBarPlace other in others)
        {
            if (other.Window != bar.Window && Yields(bar, edge, other) && MonitorIndex(monitors, other.Rect) == monitor)
                rect = Subtract(rect, other.Edge, other.Rect);
        }
        return rect;
    }

    /// <summary>The part of <paramref name="monitor"/> the bars on it leave free, which is where windows maximize.</summary>
    public static RectInt32 FreeArea(RectInt32 monitor, IReadOnlyList<RectInt32> monitors, IEnumerable<AppBarPlace> bars)
    {
        RectInt32 area = monitor;
        foreach (AppBarPlace bar in bars)
        {
            if (MonitorIndex(monitors, bar.Rect) is { } index && monitors[index] == monitor)
                area = Subtract(area, bar.Edge, bar.Rect);
        }
        return area;
    }

    /// <summary>
    /// <c>MonitorFromRect</c> with <c>MONITOR_DEFAULTTONULL</c>: the monitor the rectangle overlaps most, or null
    /// when it overlaps none (an empty rectangle overlaps nothing).
    /// </summary>
    public static int? MonitorIndex(IReadOnlyList<RectInt32> monitors, RectInt32 rect)
    {
        int? best = null;
        long bestArea = 0;
        for (int i = 0; i < monitors.Count; i++)
        {
            RectInt32 m = monitors[i];
            long width = Math.Min(Right(rect), Right(m)) - (long)Math.Max(rect.X, m.X);
            long height = Math.Min(Bottom(rect), Bottom(m)) - (long)Math.Max(rect.Y, m.Y);
            if (width > 0 && height > 0 && width * height > bestArea)
            {
                best = i;
                bestArea = width * height;
            }
        }
        return best;
    }

    /// <summary>
    /// Whether the asking bar has to stay clear of <paramref name="other"/>. Top and bottom bars take precedence:
    /// a left or right bar always stays clear of them (<c>(edge &amp; 1) != 0</c> in Explorer, which counts an unplaced
    /// bar as one too: its empty rectangle is on no monitor, so it takes nothing).
    /// </summary>
    private static bool Yields(AppBarPlace bar, AppBarEdge edge, AppBarPlace other)
    {
        if (IsHorizontal(other.Edge) && !IsHorizontal(edge))
            return true;
        // Staying on its edge, it keeps inside the bars already further out; moving to another, it goes inside them all.
        return bar.Edge == edge ? IsOutside(other, bar) : other.Edge == edge;
    }

    private static bool IsHorizontal(AppBarEdge edge) => ((uint)edge & 1) != 0;

    /// <summary>Whether <paramref name="other"/> is on the same edge as <paramref name="bar"/> and as near the screen's edge or nearer.</summary>
    private static bool IsOutside(AppBarPlace other, AppBarPlace bar) => other.Edge == bar.Edge && bar.Edge switch
    {
        AppBarEdge.Left => other.Rect.X <= bar.Rect.X,
        AppBarEdge.Top => other.Rect.Y <= bar.Rect.Y,
        AppBarEdge.Right => Right(other.Rect) >= Right(bar.Rect),
        AppBarEdge.Bottom => Bottom(other.Rect) >= Bottom(bar.Rect),
        _ => false,
    };

    /// <summary>Takes from <paramref name="rect"/> what a bar on <paramref name="edge"/> at <paramref name="bar"/> covers.</summary>
    private static RectInt32 Subtract(RectInt32 rect, AppBarEdge edge, RectInt32 bar) => edge switch
    {
        AppBarEdge.Left => FromEdges(Math.Max(rect.X, Right(bar)), rect.Y, Right(rect), Bottom(rect)),
        AppBarEdge.Top => FromEdges(rect.X, Math.Max(rect.Y, Bottom(bar)), Right(rect), Bottom(rect)),
        AppBarEdge.Right => FromEdges(rect.X, rect.Y, Math.Min(Right(rect), bar.X), Bottom(rect)),
        AppBarEdge.Bottom => FromEdges(rect.X, rect.Y, Right(rect), Math.Min(Bottom(rect), bar.Y)),
        _ => rect,
    };

    private static int Right(RectInt32 rect) => rect.X + rect.Width;

    private static int Bottom(RectInt32 rect) => rect.Y + rect.Height;

    private static RectInt32 FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);
}
