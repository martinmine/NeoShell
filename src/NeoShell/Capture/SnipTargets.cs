using Windows.Graphics;

namespace NeoShell.Capture;

/// <summary>A top-level window as the snip overlay finds it, with the part of it that's drawn, in screen pixels.</summary>
public sealed record SnipTarget(nint Handle, int ProcessId, bool OnScreen, RectInt32 Bounds);

/// <summary>
/// The windows a Window mode snip can take, as Snipping Tool's overlay offers them: every top-level window on screen
/// (tool windows, pop-ups without a title and topmost ones too, not minimized or cloaked ones), but not the shell's own
/// (the taskbar and desktop; NeoShell's windows as the shell).
/// </summary>
public static class SnipTargets
{
    /// <param name="windows">Top-level windows in z-order, topmost first.</param>
    /// <param name="shellProcessId">The shell's process, whose windows are left out.</param>
    public static IReadOnlyList<SnipTarget> Choose(IEnumerable<SnipTarget> windows, int shellProcessId) =>
        [.. windows.Where(w => w.OnScreen && w.ProcessId != shellProcessId && w.Bounds.Width > 0 && w.Bounds.Height > 0)];

    /// <summary>
    /// The topmost window at <paramref name="point"/>, or null over the desktop. Snipping Tool's place for each window
    /// reaches a pixel beyond its edges, so the pointer on the edge still picks the window.
    /// </summary>
    public static SnipTarget? At(IReadOnlyList<SnipTarget> targets, PointInt32 point) =>
        targets.FirstOrDefault(t =>
            point.X >= t.Bounds.X - 1 && point.X <= t.Bounds.X + t.Bounds.Width
            && point.Y >= t.Bounds.Y - 1 && point.Y <= t.Bounds.Y + t.Bounds.Height);
}
