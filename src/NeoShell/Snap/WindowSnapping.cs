using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Desktop;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Switcher;
using NeoShell.Taskbar;
using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// Snap as the shell (Windows leaves it to Explorer): a window dragged against a screen edge snaps to a half, to a
/// quarter near a corner, or fills the screen from the top edge, with a preview of the zone while it's held there; the
/// Snap layouts bar at the top of the screen while dragging, and the flyout on the maximize button and Win+Z; Snap
/// Assist for the space a snapped window's layout leaves; snap groups; a snapped window dragged away gets its own
/// size back; Win+arrow keys snap the window in front. All as Settings → Multitasking → Snap windows says
/// (<see cref="SnapSettings"/>).
/// </summary>
internal sealed class WindowSnapping : IDisposable
{
    private readonly WindowTracker _tracker;
    private readonly Func<ElementTheme> _theme;
    private readonly Func<nint, WallpaperWindow?> _wallpaperOn;
    private readonly WindowEvents _events = new(WindowEvent.MoveSizeStart, WindowEvent.MoveSizeEnd);
    private readonly DispatcherQueueTimer _timer;
    private readonly MaximizeButtonHover _hover;
    // The windows NeoShell snapped: where to, and their bounds (invisible borders included) from before.
    private readonly Dictionary<nint, Snapped> _snapped = [];
    private readonly SnapAssist _assist;
    private SnapPreview? _preview;
    private Drag? _drag;

    /// <param name="wallpaperOn">The wallpaper's window on a monitor, for Snap Assist and the groups' previews.</param>
    public WindowSnapping(WindowTracker tracker, Func<ElementTheme> theme, Func<nint, WallpaperWindow?> wallpaperOn)
    {
        _tracker = tracker;
        _theme = theme;
        _wallpaperOn = wallpaperOn;
        _events.Raised += OnWindowEvent;
        // The move loop is the app's own: the pointer is followed by polling, often enough for the preview to keep up.
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(30);
        _timer.Tick += (_, _) => Follow();
        _hover = new MaximizeButtonHover(SnapSettings.Read, (window, button) =>
        {
            if (_drag is null && CanSnap(window))
                ShowLayouts(window, button, keyboard: false);
        }, () => SnapLayoutsWindow.Showing?.CloseOnce());
        tracker.Changed += OnWindowsChanged;
        _assist = new SnapAssist(tracker, wallpaperOn);
    }

    /// <summary>The snap groups, for the taskbar's previews and Alt+Tab.</summary>
    public SnapGroups Groups { get; } = new();

    /// <summary>The wallpaper's window on a monitor, which the groups' previews show behind their windows.</summary>
    public nint WallpaperOn(nint monitor) => _wallpaperOn(monitor)?.Handle ?? 0;

    /// <summary>Whether snap groups show on the taskbar and in Alt+Tab, as the settings say.</summary>
    public static bool ShowGroups => SnapSettings.Read().Groups;

    /// <summary>
    /// Brings a group's windows back, as a click on its preview does: every one restored and to the front, the most
    /// recently used last (<paramref name="windows"/> is most recent first).
    /// </summary>
    public static void ActivateGroup(IReadOnlyList<nint> windows)
    {
        for (int i = windows.Count - 1; i >= 1; i--)
        {
            if (TopLevelWindows.IsMinimized(windows[i]))
                TopLevelWindows.Restore(windows[i], activate: false);
            TopLevelWindows.BringToTop(windows[i]);
        }
        TopLevelWindows.SwitchTo(windows[0]);
    }

    /// <summary>Win+arrow on the window in front.</summary>
    public void SnapForeground(SnapKey key)
    {
        nint hwnd = TopLevelWindows.GetForeground();
        if (!CanSnap(hwnd) || MonitorOf(hwnd) is not { } monitor)
            return;
        SnapPosition current = PositionOf(hwnd);
        SnapPosition position = WindowSnap.AfterKey(current, key);
        // With "Snap windows" off, only maximizing (up) and restoring or minimizing (down) are left.
        if (!SnapSettings.Read().Enabled)
        {
            if (key == SnapKey.Up && current != SnapPosition.Maximized)
                TopLevelWindows.Maximize(hwnd);
            else if (key == SnapKey.Down)
                Snap(hwnd, current == SnapPosition.Maximized ? SnapPosition.None : SnapPosition.Minimized, monitor);
            return;
        }
        Snap(hwnd, position, monitor);
        if (WindowSnap.Zone(position) is not null)
            AfterSnap(hwnd, monitor);
    }

    /// <summary>Win+Z: the Snap layouts flyout under the window's maximize button, for the keyboard.</summary>
    public void OpenLayouts(nint hwnd)
    {
        if (!SnapSettings.Read().Enabled || !CanSnap(hwnd) || MonitorOf(hwnd) is not { } monitor)
            return;
        RectInt32 button = TopLevelWindows.CaptionMaximizeButton(hwnd) ?? MaximizeButton.Find(TopLevelWindows.GetVisibleBounds(hwnd),
            monitor.Dpi, point => TopLevelWindows.AnswersMaximizeButton(hwnd, point));
        ShowLayouts(hwnd, button, keyboard: true);
    }

    public void Dispose()
    {
        _tracker.Changed -= OnWindowsChanged;
        _hover.Dispose();
        _timer.Stop();
        _events.Dispose();
        _preview?.Shut();
        _drag?.Bar?.Shut();
        _assist.Shut();
        SnapLayoutsWindow.Showing?.CloseOnce();
    }

    private static bool CanSnap(nint hwnd) =>
        hwnd != 0 && TopLevelWindows.Exists(hwnd) && !TopLevelWindows.IsDesktop(hwnd)
        && TopLevelWindows.GetProcessId(hwnd) != Environment.ProcessId && TopLevelWindows.CanResize(hwnd);

    private void ShowLayouts(nint hwnd, RectInt32 button, bool keyboard)
    {
        DisplayMonitor monitor = MonitorOf(hwnd) ?? Primary();
        IReadOnlyList<WindowInfo> suggestions = Suggestions(hwnd);
        IReadOnlyList<SnapChoice> choices = SnapLayouts.Choices(monitor.WorkArea, monitor.Dpi, suggestions.Count);
        SnapLayoutsWindow flyout = SnapLayoutsWindow.Open(hwnd, button, monitor, choices,
            [.. suggestions.Select(_tracker.WindowIcon)], keyboard, _theme(),
            (choice, zone) => SnapToChoice(hwnd, choices[choice], zone, suggestions, monitor));
        if (!keyboard)
        {
            _hover.Shown(flyout.ScreenBounds);
            flyout.Closed += (_, _) => _hover.Closed();
        }
    }

    /// <summary>
    /// Windows 11 24H2's suggestions for a layout: the most recently used windows of other apps (one each), the
    /// window's own app included when it has other windows.
    /// </summary>
    private IReadOnlyList<WindowInfo> Suggestions(nint hwnd) =>
        [.. Candidates([hwnd]).DistinctBy(TaskGrouping.Key).Take(2)];

    /// <summary>The windows that could be snapped beside others, most recently used first.</summary>
    private IReadOnlyList<WindowInfo> Candidates(IReadOnlyCollection<nint> exclude)
    {
        int ownProcess = Environment.ProcessId;
        Dictionary<nint, WindowInfo> windows = _tracker.Windows
            .Where(w => w.ProcessId != ownProcess && !exclude.Contains(w.Handle) && TopLevelWindows.CanResize(w.Handle))
            .ToDictionary(w => w.Handle);
        return [.. AltTabLayout.Order([.. windows.Keys], TopLevelWindows.GetAll(), TopLevelWindows.GetForeground()).Select(h => windows[h])];
    }

    /// <summary>
    /// A layout's zone was picked (from the flyout or the bar): the window goes there, or in a layout with suggestions
    /// into its own zone with the suggested windows in theirs; then Snap Assist for what's left.
    /// </summary>
    private void SnapToChoice(nint hwnd, SnapChoice choice, int zone, IReadOnlyList<WindowInfo> suggestions, DisplayMonitor monitor)
    {
        if (!TopLevelWindows.Exists(hwnd))
            return;
        IReadOnlyList<RectInt32> layout = [.. choice.Zones.Select(z => SnapLayouts.Bounds(z, monitor.WorkArea))];
        int own = choice.IsSuggestion ? choice.Suggested.ToList().IndexOf(-1) : zone;
        Log.Info($"Snap: 0x{hwnd:X} to zone {own + 1} of a {layout.Count}-zone layout, {WallpaperWindow.Format(layout[own])}");
        PlaceIn(hwnd, layout[own], monitor, background: false);
        for (int i = 0; i < layout.Count; i++)
        {
            int suggested = choice.Suggested[i];
            if (suggested >= 0 && suggested < suggestions.Count && TopLevelWindows.Exists(suggestions[suggested].Handle))
                PlaceIn(suggestions[suggested].Handle, layout[i], monitor, background: true);
        }
        TopLevelWindows.Activate(hwnd);
        Complete(hwnd, layout, monitor);
    }

    /// <summary>A window snapped to a half or a quarter: the layout it starts or completes, as Explorer finds it.</summary>
    private void AfterSnap(nint hwnd, DisplayMonitor monitor)
    {
        if (!_snapped.TryGetValue(hwnd, out Snapped? snapped))
            return;
        IReadOnlyList<IReadOnlyList<RectInt32>> layouts = SnapAssistPlan.Layouts(monitor.WorkArea, monitor.Dpi);
        if (SnapAssistPlan.EmptyZones(layouts, snapped.Zone, [.. Filled(monitor, hwnd).Keys]) is { } plan)
            Complete(hwnd, plan.Layout, monitor);
    }

    /// <summary>
    /// The windows in a layout's zones form a group; Snap Assist offers the rest of the windows for its empty zones.
    /// </summary>
    private void Complete(nint hwnd, IReadOnlyList<RectInt32> layout, DisplayMonitor monitor)
    {
        JoinLayout(layout, monitor);
        Dictionary<RectInt32, nint> filled = Filled(monitor, except: 0);
        List<RectInt32> empty = [.. layout.Where(zone => !filled.ContainsKey(zone))];
        if (empty.Count == 0 || !SnapSettings.Read().Assist)
            return;
        _assist.Offer(monitor, empty, hwnd, _theme(),
            () => Candidates([.. Filled(monitor, except: 0).Where(f => layout.Contains(f.Key)).Select(f => f.Value)]),
            (window, zone) =>
            {
                PlaceIn(window, zone, monitor, background: true);
                JoinLayout(layout, monitor);
            });
    }

    private void JoinLayout(IReadOnlyList<RectInt32> layout, DisplayMonitor monitor)
    {
        List<nint> members = [.. Filled(monitor, except: 0).Where(f => layout.Contains(f.Key)).Select(f => f.Value)];
        if (members.Count >= 2)
            Groups.Join(members);
    }

    /// <summary>The zones snapped windows fill on the monitor (where each is drawn exactly), and their windows.</summary>
    private Dictionary<RectInt32, nint> Filled(DisplayMonitor monitor, nint except)
    {
        var filled = new Dictionary<RectInt32, nint>();
        foreach ((nint window, Snapped snapped) in _snapped)
        {
            if (window != except && TopLevelWindows.Exists(window) && !TopLevelWindows.IsMinimized(window)
                && !TopLevelWindows.IsMaximized(window) && TopLevelWindows.MonitorOf(window) == monitor.Handle
                && TopLevelWindows.GetVisibleBounds(window) == snapped.Zone)
            {
                filled.TryAdd(snapped.Zone, window);
            }
        }
        return filled;
    }

    /// <summary>Puts a window in a zone of the monitor and remembers it as snapped there.</summary>
    /// <param name="background">Not brought to the front (the suggested windows, Snap Assist's).</param>
    private void PlaceIn(nint hwnd, RectInt32 zone, DisplayMonitor monitor, bool background)
    {
        _snapped.TryGetValue(hwnd, out Snapped? snapped);
        bool restored = !TopLevelWindows.IsMaximized(hwnd) && !TopLevelWindows.IsMinimized(hwnd);
        RectInt32 before = snapped?.Restore ?? (restored ? TopLevelWindows.GetBounds(hwnd) : default);
        if (background)
            TopLevelWindows.PlaceInBackground(hwnd, zone);
        else
            TopLevelWindows.Place(hwnd, zone);
        if (snapped?.Zone != zone)
            Groups.Leave(hwnd);
        // A window that was maximized or minimized keeps the size it gets here when dragged away.
        _snapped[hwnd] = new Snapped(PositionFor(zone, monitor), zone, before.Width > 0 ? before : TopLevelWindows.GetBounds(hwnd));
    }

    private void OnWindowEvent(WindowEvent kind, nint hwnd)
    {
        if (kind == WindowEvent.MoveSizeStart)
            Start(hwnd);
        else if (kind == WindowEvent.MoveSizeEnd && _drag?.Window == hwnd)
            End();
    }

    // Closed windows leave their groups.
    private void OnWindowsChanged()
    {
        var open = _tracker.Windows.Select(w => w.Handle).ToHashSet();
        foreach (nint window in Groups.All.SelectMany(group => group).Where(w => !open.Contains(w)).ToList())
            Groups.Leave(window);
        foreach (nint window in _snapped.Keys.Where(w => !TopLevelWindows.Exists(w)).ToList())
            _snapped.Remove(window);
    }

    private void Start(nint hwnd)
    {
        SnapSettings settings = SnapSettings.Read();
        if (!settings.Enabled || !CanSnap(hwnd))
            return;
        SnapLayoutsWindow.Showing?.CloseOnce();
        _assist.Dismiss();
        RectInt32 bounds = TopLevelWindows.GetBounds(hwnd);
        PointInt32 pointer = Cursor.Position();
        double grab = bounds.Width > 0 ? Math.Clamp((pointer.X - bounds.X) / (double)bounds.Width, 0, 1) : 0.5;
        _drag = new Drag(hwnd, bounds, TopLevelWindows.IsMaximized(hwnd), grab, settings);
        _timer.Start();
        WidenClip();
    }

    /// <summary>
    /// Opens the widget sidebar's strip to the pointer: Windows keeps it inside the work area while a window moves,
    /// which left windows unable to go over the sidebar to the screen's edge. Windows frees it when the move ends.
    /// </summary>
    private static void WidenClip()
    {
        RectInt32 clip = Cursor.Clip;
        RectInt32 wider = clip;
        foreach (DisplayMonitor monitor in DisplayMonitor.GetAll())
        {
            RectInt32 area = ShellWorkArea.DragArea(monitor.Bounds);
            if (Overlaps(area, clip))
                wider = Union(wider, area);
        }
        if (wider != clip)
            Cursor.Clip = wider;
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
            drag.Bar?.Leave();
            drag.Bar = null;
            ShowTarget(drag, null);
            return;
        }

        PointInt32 pointer = Cursor.Position();
        DisplayMonitor? monitor = DisplayMonitor.GetAll().FirstOrDefault(m => Contains(m.Bounds, pointer));
        // The bar comes once the window is on the move (a click on a title bar starts a move loop too).
        if (drag.Bar is null && drag.Settings.SnapBar && monitor is not null && (bounds.X != drag.Start.X || bounds.Y != drag.Start.Y))
        {
            drag.Suggestions = Suggestions(drag.Window);
            drag.Bar = new SnapBar(monitor, SnapLayouts.Choices(monitor.WorkArea, monitor.Dpi, Math.Min(1, drag.Suggestions.Count)),
                [.. drag.Suggestions.Select(_tracker.WindowIcon)], _theme());
            drag.Bar.Peek();
        }

        drag.BarZone = drag.Bar?.Follow(pointer);
        drag.Position = SnapPosition.None;
        drag.Monitor = monitor;
        RectInt32? zone = null;
        if (drag.BarZone is { } picked && drag.Bar is { } bar)
        {
            zone = SnapLayouts.Bounds(bar.Choices[picked.Choice].Zones[OwnZone(bar.Choices[picked.Choice], picked.Zone)], bar.Monitor.WorkArea);
            drag.Monitor = bar.Monitor;
        }
        else if (monitor is not null)
        {
            drag.Position = WindowSnap.AtPointer(pointer, ShellWorkArea.DragArea(monitor.Bounds), monitor.Dpi, drag.Settings.NearEdge);
            if (drag.Position != SnapPosition.None)
                zone = ZoneBounds(drag.Position, monitor);
        }
        ShowTarget(drag, zone);
    }

    // Where the window itself goes in a layout: the zone picked, or in one with suggestions, its own zone.
    private static int OwnZone(SnapChoice choice, int zone) => choice.IsSuggestion ? choice.Suggested.ToList().IndexOf(-1) : zone;

    private void ShowTarget(Drag drag, RectInt32? zone)
    {
        if (zone == drag.Shown)
            return;
        drag.Shown = zone;
        if (zone is not { } bounds || drag.Monitor is not { } monitor)
        {
            _preview?.Hide();
            return;
        }
        _preview ??= new SnapPreview();
        _preview.Show(bounds, monitor.Dpi, drag.Window, _theme());
    }

    /// <summary>The window was let go: it snaps where the preview showed, or a snapped one dragged away gets its size back.</summary>
    private void End()
    {
        _timer.Stop();
        _preview?.Hide();
        if (_drag is not { } drag)
            return;
        _drag = null;
        drag.Bar?.Leave();
        nint hwnd = drag.Window;
        if (!TopLevelWindows.Exists(hwnd))
            return;

        if (drag.Resizing)
        {
            // Sized by hand: not snapped any more.
            _snapped.Remove(hwnd);
            Groups.Leave(hwnd);
        }
        else if (drag.BarZone is { } picked && drag.Bar is { } bar)
        {
            SnapToChoice(hwnd, bar.Choices[picked.Choice], picked.Zone, drag.Suggestions, bar.Monitor);
        }
        else if (drag.Position != SnapPosition.None && drag.Monitor is { } monitor)
        {
            Snap(hwnd, drag.Position, monitor, before: _snapped.TryGetValue(hwnd, out Snapped? snapped) ? snapped.Restore : drag.Start);
            if (drag.Position != SnapPosition.Maximized)
                AfterSnap(hwnd, monitor);
        }
        else if (_snapped.TryGetValue(hwnd, out Snapped? was))
        {
            // A click on the title bar starts a move too; only one that went somewhere unsnaps.
            RectInt32 dropped = TopLevelWindows.GetBounds(hwnd);
            if (dropped.X == drag.Start.X && dropped.Y == drag.Start.Y)
                return;
            _snapped.Remove(hwnd);
            Groups.Leave(hwnd);
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
                Groups.Leave(hwnd);
                if (snapped is not null)
                    TopLevelWindows.SetBounds(hwnd, snapped.Restore);
                break;
            default:
                if (TopLevelWindows.IsMaximized(hwnd))
                    TopLevelWindows.RestoreNow(hwnd);
                RectInt32 restore = before ?? snapped?.Restore ?? TopLevelWindows.GetBounds(hwnd);
                RectInt32 zone = ZoneBounds(position, monitor);
                TopLevelWindows.Place(hwnd, zone);
                if (snapped?.Zone != zone)
                    Groups.Leave(hwnd);
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

    // A layout's zone that is a half or a quarter counts as one for Win+arrows.
    private static SnapPosition PositionFor(RectInt32 zone, DisplayMonitor monitor) =>
        Enum.GetValues<SnapPosition>().FirstOrDefault(p => WindowSnap.Zone(p) is not null && ZoneBounds(p, monitor) == zone);

    private static RectInt32 ZoneBounds(SnapPosition position, DisplayMonitor monitor) =>
        WindowSnap.Zone(position) is { } zone ? SnapLayouts.Bounds(zone, monitor.WorkArea) : monitor.WorkArea;

    private static DisplayMonitor? MonitorOf(nint hwnd)
    {
        nint handle = TopLevelWindows.MonitorOf(hwnd);
        return DisplayMonitor.GetAll().FirstOrDefault(monitor => monitor.Handle == handle);
    }

    private static DisplayMonitor Primary()
    {
        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    private static bool Overlaps(RectInt32 a, RectInt32 b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static RectInt32 Union(RectInt32 a, RectInt32 b)
    {
        int left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        return new RectInt32(left, top, Math.Max(a.X + a.Width, b.X + b.Width) - left, Math.Max(a.Y + a.Height, b.Y + b.Height) - top);
    }

    private static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    /// <summary>A snapped window: the position, the zone it fills (what's drawn of it) and its bounds from before.</summary>
    private sealed record Snapped(SnapPosition Position, RectInt32 Zone, RectInt32 Restore);

    /// <summary>A window being moved or sized by the user.</summary>
    private sealed class Drag(nint window, RectInt32 start, bool wasMaximized, double grab, SnapSettings settings)
    {
        public nint Window { get; } = window;
        /// <summary>Its bounds as the drag began (or as Windows restored it from maximized).</summary>
        public RectInt32 Start { get; set; } = start;
        public bool WasMaximized { get; set; } = wasMaximized;
        /// <summary>Where along its width it was grabbed, from 0 to 1.</summary>
        public double Grab { get; } = grab;
        /// <summary>The settings as the drag began.</summary>
        public SnapSettings Settings { get; } = settings;
        public bool Resizing { get; set; }
        /// <summary>Where it snaps by the screen's edges if let go now.</summary>
        public SnapPosition Position { get; set; }
        public DisplayMonitor? Monitor { get; set; }
        /// <summary>The Snap bar, once the window moves, and the zone of it under the pointer.</summary>
        public SnapBar? Bar { get; set; }
        public (int Choice, int Zone)? BarZone { get; set; }
        public IReadOnlyList<WindowInfo> Suggestions { get; set; } = [];
        /// <summary>The zone the preview shows.</summary>
        public RectInt32? Shown { get; set; }
    }
}
