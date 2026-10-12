using Microsoft.UI.Dispatching;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Tray;

/// <summary>
/// Other apps' app bars (<c>SHAppBarMessage</c>), served while NeoShell is the shell as Explorer's tray serves them:
/// bars are registered, fitted against the taskbar, the widget sidebar and each other (<see cref="AppBarLayout"/>),
/// keep their space in the work area (<see cref="ShellWorkArea"/>), and are told when the space or a full-screen app
/// changes. Lives on the UI thread, where <c>Shell_TrayWnd</c> receives the messages.
/// </summary>
internal sealed class AppBars : IDisposable
{
    private const nint ABS_AUTOHIDE = 1;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly NotificationArea _tray;
    private readonly Func<RectInt32?> _taskbarBounds;
    private readonly Func<bool> _taskbarAutoHides;
    // In the order they registered, as Explorer keeps them.
    private readonly List<Bar> _bars = [];
    // Per monitor, the auto-hide bar on each edge (ABE_* index), 0 for none.
    private readonly Dictionary<nint, nint[]> _autoHideBars = [];

    /// <param name="taskbarBounds">Where the primary taskbar is when shown.</param>
    /// <param name="taskbarAutoHides">Whether the taskbar hides itself (<c>ABM_GETSTATE</c>).</param>
    public AppBars(NotificationArea tray, Func<RectInt32?> taskbarBounds, Func<bool> taskbarAutoHides)
    {
        _tray = tray;
        _taskbarBounds = taskbarBounds;
        _taskbarAutoHides = taskbarAutoHides;
        _tray.AppBarMessageHandler = OnMessage;
        _tray.WorkAreaChanged += OnWorkAreaSet;
        ShellWorkArea.Changed += OnReservationChanged;
    }

    /// <summary>Tells the bars the taskbar's auto-hide state changed (<c>ABN_STATECHANGE</c>).</summary>
    public void NotifyStateChange() => Notify(0, AppBarNotification.StateChange, 0, except: 0);

    /// <summary>
    /// A full-screen app is in front on <paramref name="monitor"/> (0: none). Each bar on a monitor whose state
    /// changed is told once (<c>ABN_FULLSCREENAPP</c>), as Explorer remembers what it last told each.
    /// </summary>
    public void SetFullScreenMonitor(nint monitor)
    {
        foreach (Bar bar in _bars.ToList())
        {
            if (!TopLevelWindows.Exists(bar.Window))
            {
                Delete(bar);
                continue;
            }
            bool fullScreen = monitor != 0 && TopLevelWindows.NearestMonitorOf(bar.Window) == monitor;
            if (fullScreen != bar.FullScreen)
            {
                bar.FullScreen = fullScreen;
                WindowMessages.Post(bar.Window, bar.CallbackMessage, (nint)AppBarNotification.FullScreenApp, fullScreen ? 1 : 0);
            }
        }
    }

    public void Dispose()
    {
        ShellWorkArea.Changed -= OnReservationChanged;
        _tray.WorkAreaChanged -= OnWorkAreaSet;
        _tray.AppBarMessageHandler = null;
        foreach (Bar bar in _bars)
            bar.Thread?.Dispose();
        _bars.Clear();
        // The space goes back with NeoShell (ShellWorkArea.Restore); Explorer, coming back, has the bars register again.
        foreach (DisplayMonitor monitor in DisplayMonitor.GetAll())
            ShellWorkArea.ReserveAppBars(monitor.Bounds, monitor.Bounds);
    }

    /// <summary>One <c>SHAppBarMessage</c> call; returns what the caller gets back, as Explorer's does.</summary>
    private nint OnMessage(AppBarMessage message)
    {
        switch (message.Command)
        {
            case AppBarCommand.New:
                return Add(message.Window, message.CallbackMessage) ? 1 : 0;
            case AppBarCommand.Remove:
                foreach (Bar bar in _bars.Where(bar => bar.Window == message.Window).ToList())
                    Delete(bar);
                return 1;
            case AppBarCommand.QueryPos:
                if (Find(message.Window) is { } asking)
                    message.ReplyRect(QueryPos(asking, message.Edge, message.Rect));
                return 1;
            case AppBarCommand.SetPos:
                if (Find(message.Window) is { } moving)
                    SetPos(moving, message);
                return 1;
            case AppBarCommand.GetState:
                // Never ABS_ALWAYSONTOP: Windows 11's taskbar is always on top, and Explorer doesn't say so.
                return _taskbarAutoHides() ? ABS_AUTOHIDE : 0;
            case AppBarCommand.GetTaskbarPos:
                if (_taskbarBounds() is { } taskbar)
                    message.ReplyTaskbarPos(AppBarEdge.Bottom, taskbar);
                return 1;
            case AppBarCommand.Activate or AppBarCommand.WindowPosChanged:
                Activate(message.Window);
                return 1;
            case AppBarCommand.GetAutoHideBar:
                return AutoHideBar(message.Edge, TaskbarMonitor());
            case AppBarCommand.GetAutoHideBarEx:
                return AutoHideBar(message.Edge, DisplayMonitor.HandleFromRect(message.Rect, nearest: true));
            case AppBarCommand.SetAutoHideBar or AppBarCommand.SetAutoHideBarEx:
                nint monitor = message.Command == AppBarCommand.SetAutoHideBar ? TaskbarMonitor() : DisplayMonitor.HandleFromRect(message.Rect, nearest: true);
                return (uint)message.Edge < 4 && SetAutoHideBar(message.Window, message.LParam != 0, message.Edge, monitor) ? 1 : 0;
            case AppBarCommand.SetState:
                // Explorer (26200) accepts ABS_AUTOHIDE here but no longer changes anything; neither does NeoShell.
                return 1;
            default:
                return 0;
        }
    }

    private Bar? Find(nint window) => _bars.FirstOrDefault(bar => bar.Window == window);

    private bool Add(nint window, uint callbackMessage)
    {
        if (Find(window) is not null)
            return false;
        // Gone without ABM_REMOVE (crashed, killed): its space is given back once its thread ends.
        var bar = new Bar(window, callbackMessage);
        bar.Thread = WindowThread.Watch(window, () => _dispatcher.TryEnqueue(() =>
        {
            if (_bars.Contains(bar) && !TopLevelWindows.Exists(window))
                Delete(bar);
        }));
        _bars.Add(bar);
        return true;
    }

    private void Delete(Bar bar)
    {
        _bars.Remove(bar);
        bar.Thread?.Dispose();
        Moved(bar.Window, bar.Rect, null);
    }

    private RectInt32 QueryPos(Bar bar, AppBarEdge edge, RectInt32 proposed)
    {
        IReadOnlyList<RectInt32> monitors = Monitors();
        var taskbars = new List<RectInt32>();
        var others = _bars.Where(other => other != bar).Select(other => other.Place).ToList();
        foreach (RectInt32 monitor in monitors)
        {
            int bottom = ShellWorkArea.TaskbarHeight(monitor);
            if (bottom > 0)
                taskbars.Add(monitor with { Y = monitor.Y + monitor.Height - bottom, Height = bottom });
        }
        return AppBarLayout.QueryPos(bar.Place, edge, proposed, monitors, taskbars, others);
    }

    private void SetPos(Bar bar, AppBarMessage message)
    {
        RectInt32 rect = QueryPos(bar, message.Edge, message.Rect);
        message.ReplyRect(rect);
        // Explorer keeps the bar's old edge too when its rectangle stays the same.
        if (rect == bar.Rect)
            return;
        RectInt32 old = bar.Rect;
        bar.Rect = rect;
        bar.Edge = message.Edge;
        Moved(bar.Window, old, rect);
    }

    /// <summary>
    /// A bar moved or went (<c>CTray::StuckAppChange</c>): the bars on the monitors it left and joined are told, but
    /// not the bar itself, and the work area there is worked out again.
    /// </summary>
    private void Moved(nint window, RectInt32 from, RectInt32? to)
    {
        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        nint left = from.Width > 0 && from.Height > 0 ? DisplayMonitor.HandleFromRect(from, nearest: true) : 0;
        nint joined = to is { } rect ? DisplayMonitor.HandleFromRect(rect) : 0;
        foreach (DisplayMonitor monitor in monitors.Where(monitor => monitor.Handle != 0 && (monitor.Handle == left || monitor.Handle == joined)))
        {
            Notify(monitor.Handle, AppBarNotification.PosChanged, 0, except: window);
            ShellWorkArea.ReserveAppBars(monitor.Bounds, FreeArea(monitor.Bounds));
        }
    }

    private RectInt32 FreeArea(RectInt32 monitor) => AppBarLayout.FreeArea(monitor, Monitors(), _bars.Select(bar => bar.Place));

    /// <summary>
    /// What a monitor's work area is made of changed: when the displays changed, a monitor that's new to the bars gets
    /// their space.
    /// </summary>
    private void OnReservationChanged(RectInt32 monitor) => ShellWorkArea.ReserveAppBars(monitor, FreeArea(monitor));

    /// <summary>
    /// A work area was set, by NeoShell (a bar, the taskbar or the sidebar moved) or another app: every bar is told, as
    /// Explorer's tray does on <c>WM_SETTINGCHANGE</c> (twice, there).
    /// </summary>
    private void OnWorkAreaSet() => Notify(0, AppBarNotification.PosChanged, 0, except: 0);

    /// <summary>Posts a notification to the bars on <paramref name="monitor"/> (0: all), dropping bars whose window is gone.</summary>
    private void Notify(nint monitor, AppBarNotification notification, nint lParam, nint except)
    {
        foreach (Bar bar in _bars.ToList())
        {
            if (bar.Window == except)
                continue;
            if (!TopLevelWindows.Exists(bar.Window))
                Delete(bar);
            else if (monitor == 0 || TopLevelWindows.NearestMonitorOf(bar.Window) == monitor)
                WindowMessages.Post(bar.Window, bar.CallbackMessage, (nint)notification, lParam);
        }
    }

    /// <summary>
    /// <c>ABM_ACTIVATE</c> and <c>ABM_WINDOWPOSCHANGED</c>: a bar coming forward lifts the auto-hide bar on its edge
    /// above it, so that one still slides out over it.
    /// </summary>
    private void Activate(nint window)
    {
        if (Find(window) is not { } bar || !_autoHideBars.TryGetValue(TopLevelWindows.NearestMonitorOf(window), out nint[]? edges))
            return;
        // The auto-hide bar of another edge, itself coming forward.
        for (int edge = 0; edge < edges.Length; edge++)
        {
            if (edges[edge] == window && edge != (int)bar.Edge)
                return;
        }
        if ((uint)bar.Edge >= 4)
            return;
        nint autoHide = edges[(int)bar.Edge];
        if (autoHide != 0 && !TopLevelWindows.Exists(autoHide))
            edges[(int)bar.Edge] = 0;
        else if (autoHide != 0 && autoHide != window)
            _dispatcher.TryEnqueue(() => TopLevelWindows.BringToTop(autoHide));
    }

    private nint AutoHideBar(AppBarEdge edge, nint monitor)
    {
        if ((uint)edge >= 4 || !_autoHideBars.TryGetValue(monitor, out nint[]? edges))
            return 0;
        nint bar = edges[(int)edge];
        if (TopLevelWindows.Exists(bar))
            return bar;
        edges[(int)edge] = 0;
        return 0;
    }

    /// <summary>
    /// Sets or clears the auto-hide bar on an edge (<c>CTray::AppBarSetAutoHideBar</c>): one per edge and monitor, so
    /// it fails when another bar has the edge. Published on <c>Shell_TrayWnd</c> for shell32 as Explorer does,
    /// including its quirk: a bar setting itself again publishes "none", and shell32 then answers 0 for that edge.
    /// </summary>
    private bool SetAutoHideBar(nint window, bool set, AppBarEdge edge, nint monitor)
    {
        int index = (int)edge;
        nint published = 1;
        bool done;
        _autoHideBars.TryGetValue(monitor, out nint[]? edges);
        if (edges is not null && !TopLevelWindows.Exists(edges[index]))
            edges[index] = 0;

        if (!set)
        {
            if (edges is not null)
                edges[index] = 0;
            done = true;
        }
        else if (edges is null)
        {
            edges = new nint[4];
            edges[index] = published = window;
            _autoHideBars[monitor] = edges;
            done = true;
        }
        else
        {
            if (edges[index] == 0)
                edges[index] = published = window;
            done = edges[index] == window;
        }
        _tray.PublishAutoHideBar(TaskbarMonitor(), monitor, edge, published);
        return done;
    }

    /// <summary>Monitor bounds, the primary monitor first (for <c>MONITOR_DEFAULTTOPRIMARY</c>).</summary>
    private static IReadOnlyList<RectInt32> Monitors() =>
        [.. DisplayMonitor.GetAll().OrderByDescending(monitor => monitor.IsPrimary).Select(monitor => monitor.Bounds)];

    /// <summary>The taskbar's monitor, the primary one: where the tray is.</summary>
    private static nint TaskbarMonitor() => DisplayMonitor.GetAll().FirstOrDefault(monitor => monitor.IsPrimary)?.Handle ?? 0;

    private sealed class Bar(nint window, uint callbackMessage)
    {
        public nint Window { get; } = window;
        public uint CallbackMessage { get; } = callbackMessage;
        public AppBarEdge Edge { get; set; } = AppBarEdge.None;
        public RectInt32 Rect { get; set; }
        /// <summary>What the bar was last told about full-screen apps.</summary>
        public bool FullScreen { get; set; }
        public WindowThread? Thread { get; set; }
        public AppBarPlace Place => new(Window, Edge, Rect);
    }
}
