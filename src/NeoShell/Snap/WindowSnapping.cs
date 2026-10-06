using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// Snap as the shell (Windows leaves it to Explorer): a window dragged against a screen edge snaps to a half, to a
/// quarter near a corner, or fills the screen from the top edge, with a preview of the zone while it's held there; a
/// snapped window dragged away gets its own size back; Win+arrow keys snap the window in front.
/// </summary>
internal sealed class WindowSnapping : IDisposable
{
    private readonly Func<ElementTheme> _theme;
    private readonly WindowEvents _events = new(WindowEvent.MoveSizeStart, WindowEvent.MoveSizeEnd);
    private readonly DispatcherQueueTimer _timer;
    // The windows NeoShell snapped: where to, and their bounds (invisible borders included) from before.
    private readonly Dictionary<nint, Snapped> _snapped = [];
    private SnapPreview? _preview;
    private Drag? _drag;

    public WindowSnapping(Func<ElementTheme> theme)
    {
        _theme = theme;
        _events.Raised += OnWindowEvent;
        // The move loop is the app's own: the pointer is followed by polling, often enough for the preview to keep up.
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(30);
        _timer.Tick += (_, _) => Follow();
    }

    /// <summary>Win+arrow on the window in front.</summary>
    public void SnapForeground(SnapKey key)
    {
        nint hwnd = TopLevelWindows.GetForeground();
        if (!CanSnap(hwnd) || MonitorOf(hwnd) is not { } monitor)
            return;
        Snap(hwnd, WindowSnap.AfterKey(PositionOf(hwnd), key), monitor);
    }

    public void Dispose()
    {
        _timer.Stop();
        _events.Dispose();
        _preview?.Close();
    }

    private static bool CanSnap(nint hwnd) =>
        hwnd != 0 && TopLevelWindows.Exists(hwnd) && !TopLevelWindows.IsDesktop(hwnd)
        && TopLevelWindows.GetProcessId(hwnd) != Environment.ProcessId && TopLevelWindows.CanResize(hwnd);

    private void OnWindowEvent(WindowEvent kind, nint hwnd)
    {
        if (kind == WindowEvent.MoveSizeStart)
            Start(hwnd);
        else if (kind == WindowEvent.MoveSizeEnd && _drag?.Window == hwnd)
            End();
    }

    private void Start(nint hwnd)
    {
        if (!CanSnap(hwnd))
            return;
        RectInt32 bounds = TopLevelWindows.GetBounds(hwnd);
        PointInt32 pointer = Cursor.Position();
        double grab = bounds.Width > 0 ? Math.Clamp((pointer.X - bounds.X) / (double)bounds.Width, 0, 1) : 0.5;
        _drag = new Drag(hwnd, bounds, TopLevelWindows.IsMaximized(hwnd), grab);
        _timer.Start();
    }

    /// <summary>Shows where the window would snap if let go now, unless it's being resized rather than moved.</summary>
    private void Follow()
    {
        if (_drag is not { } drag || !TopLevelWindows.Exists(drag.Window))
        {
            End();
            return;
        }

        RectInt32 bounds = TopLevelWindows.GetBounds(drag.Window);
        // Dragging a maximized window restores it (Windows does that much itself); its size counts from then.
        if (drag.WasMaximized && !TopLevelWindows.IsMaximized(drag.Window))
        {
            drag.WasMaximized = false;
            drag.Start = bounds;
        }
        if (drag.WasMaximized)
            return;
        if (bounds.Width != drag.Start.Width || bounds.Height != drag.Start.Height)
        {
            // Sized, not moved: nothing to snap until it's let go.
            drag.Resizing = true;
            _timer.Stop();
            ShowTarget(drag, SnapPosition.None, null);
            return;
        }

        PointInt32 pointer = Cursor.Position();
        DisplayMonitor? monitor = DisplayMonitor.GetAll().FirstOrDefault(m => Contains(m.Bounds, pointer));
        SnapPosition target = monitor is null ? SnapPosition.None : WindowSnap.AtPointer(pointer, monitor.WorkArea, monitor.Dpi);
        ShowTarget(drag, target, monitor);
    }

    private void ShowTarget(Drag drag, SnapPosition target, DisplayMonitor? monitor)
    {
        if (target == drag.Target && monitor?.Handle == drag.Monitor?.Handle)
            return;
        drag.Target = target;
        drag.Monitor = monitor;
        if (target == SnapPosition.None || monitor is null)
        {
            _preview?.Hide();
            return;
        }
        _preview ??= new SnapPreview();
        _preview.Show(ZoneBounds(target, monitor), monitor.Dpi, drag.Window, _theme());
    }

    /// <summary>The window was let go: it snaps where the preview showed, or a snapped one dragged away gets its size back.</summary>
    private void End()
    {
        _timer.Stop();
        _preview?.Hide();
        if (_drag is not { } drag)
            return;
        _drag = null;
        nint hwnd = drag.Window;
        if (!TopLevelWindows.Exists(hwnd))
            return;

        if (drag.Resizing)
        {
            _snapped.Remove(hwnd); // sized by hand: not snapped any more
        }
        else if (drag.Target != SnapPosition.None && drag.Monitor is { } monitor)
        {
            Snap(hwnd, drag.Target, monitor, before: _snapped.TryGetValue(hwnd, out Snapped? snapped) ? snapped.Restore : drag.Start);
        }
        else if (_snapped.TryGetValue(hwnd, out Snapped? was))
        {
            // A click on the title bar starts a move too; only one that went somewhere unsnaps.
            RectInt32 dropped = TopLevelWindows.GetBounds(hwnd);
            if (dropped.X == drag.Start.X && dropped.Y == drag.Start.Y)
                return;
            _snapped.Remove(hwnd);
            TopLevelWindows.SetBounds(hwnd, WindowSnap.Unsnapped(dropped, was.Restore, Cursor.Position().X, drag.Grab));
        }
    }

    /// <summary>Puts the window in the position on the monitor; restoring it from a half or a quarter gives it its own bounds back.</summary>
    /// <param name="before">Its bounds before it was snapped, when known; otherwise where it is now.</param>
    private void Snap(nint hwnd, SnapPosition position, DisplayMonitor monitor, RectInt32? before = null)
    {
        _snapped.TryGetValue(hwnd, out Snapped? snapped);
        switch (position)
        {
            case SnapPosition.Maximized:
                // A snapped window stays snapped underneath, as in Windows: restored, it's back in its zone.
                TopLevelWindows.Maximize(hwnd);
                break;
            case SnapPosition.Minimized:
                TopLevelWindows.Minimize(hwnd);
                break;
            case SnapPosition.None when TopLevelWindows.IsMaximized(hwnd):
                TopLevelWindows.RestoreNow(hwnd);
                break;
            case SnapPosition.None:
                _snapped.Remove(hwnd);
                if (snapped is not null)
                    TopLevelWindows.SetBounds(hwnd, snapped.Restore);
                break;
            default:
                if (TopLevelWindows.IsMaximized(hwnd))
                    TopLevelWindows.RestoreNow(hwnd);
                RectInt32 restore = before ?? snapped?.Restore ?? TopLevelWindows.GetBounds(hwnd);
                RectInt32 zone = ZoneBounds(position, monitor);
                TopLevelWindows.Place(hwnd, zone);
                _snapped[hwnd] = new Snapped(position, zone, restore);
                break;
        }
    }

    /// <summary>Where the window is snapped: maximized, or where NeoShell put it if it's still exactly there.</summary>
    private SnapPosition PositionOf(nint hwnd)
    {
        if (TopLevelWindows.IsMaximized(hwnd))
            return SnapPosition.Maximized;
        return _snapped.TryGetValue(hwnd, out Snapped? snapped) && TopLevelWindows.GetVisibleBounds(hwnd) == snapped.Zone
            ? snapped.Position
            : SnapPosition.None;
    }

    private static RectInt32 ZoneBounds(SnapPosition position, DisplayMonitor monitor) =>
        WindowSnap.Zone(position) is { } zone ? SnapLayouts.Bounds(zone, monitor.WorkArea) : monitor.WorkArea;

    private static DisplayMonitor? MonitorOf(nint hwnd)
    {
        nint handle = TopLevelWindows.MonitorOf(hwnd);
        return DisplayMonitor.GetAll().FirstOrDefault(monitor => monitor.Handle == handle);
    }

    private static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    /// <summary>A snapped window: the position, the zone it fills (what's drawn of it) and its bounds from before.</summary>
    private sealed record Snapped(SnapPosition Position, RectInt32 Zone, RectInt32 Restore);

    /// <summary>A window being moved or sized by the user.</summary>
    private sealed class Drag(nint window, RectInt32 start, bool wasMaximized, double grab)
    {
        public nint Window { get; } = window;
        /// <summary>Its bounds as the drag began (or as Windows restored it from maximized).</summary>
        public RectInt32 Start { get; set; } = start;
        public bool WasMaximized { get; set; } = wasMaximized;
        /// <summary>Where along its width it was grabbed, from 0 to 1.</summary>
        public double Grab { get; } = grab;
        public bool Resizing { get; set; }
        public SnapPosition Target { get; set; }
        public DisplayMonitor? Monitor { get; set; }
    }
}
