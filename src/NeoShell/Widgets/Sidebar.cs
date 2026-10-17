using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Taskbar;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Widgets;

/// <summary>
/// The widgets: those docked in the sidebar on the primary monitor, and those dragged out of it to float on the
/// desktop. <see cref="ShellSettings.Widgets"/> keeps them all; the sidebar shows the docked ones in their order.
/// </summary>
internal sealed class Sidebar : IDisposable
{
    // Effective pixels between a floating widget put back on screen and the work area's corner.
    private const int ScreenMargin = 24;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly RunMode _runMode;
    private readonly SettingsStore _settings;
    private readonly Taskbars _taskbars;
    private readonly ResourceMonitor _resources = new();
    private readonly WirelessMonitor _wireless = new();
    private readonly Dictionary<string, FloatingWidgetWindow> _floating = [];
    private SidebarWindow? _window;
    private Backdrop _backdrop;
    // The 2088 concept themes the widgets too: see-through and dark, its own cards (WidgetFrame.SetConcept).
    private bool _concept;
    // A docked widget dragged off the sidebar: its window on the desktop, shown while the pointer is off the sidebar.
    private FloatingWidgetWindow? _dragOut;
    private Point _grab;
    // A floating widget dragged over the sidebar: a card there showing it where it would go; let go there, it stays as
    // the widget's card.
    private WidgetFrame? _slot;

    public Sidebar(RunMode runMode, SettingsStore settings, Taskbars taskbars)
    {
        _runMode = runMode;
        _settings = settings;
        _taskbars = taskbars;
        _backdrop = settings.Current.TaskbarBackdrop;
        _concept = settings.Current.Theme == ShellTheme.Concept2088;
    }

    // What the windows are drawn with: the taskbar's backdrop, theme and accent, or the concept's.
    private Backdrop Backdrop => _concept ? Backdrop.Transparent : _backdrop;

    private ElementTheme Theme => _concept ? ElementTheme.Dark : _taskbars.Theme;

    private Windows.UI.Color? Accent => _concept ? null : _taskbars.Accent;

    private IReadOnlyList<WidgetSettings> Widgets => _settings.Current.Widgets;

    public void Show()
    {
        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        foreach (WidgetSettings widget in Widgets.Where(w => w.IsFloating))
        {
            PointInt32 topLeft = KeepOnScreen(new PointInt32(widget.X!.Value, widget.Y!.Value), monitors);
            ShowFloating(widget, topLeft);
        }
        if (_settings.Current.ShowWidgetSidebar)
            CreateWindow();

        _settings.Changed += OnSettingsChanged;
        _taskbars.Updated += OnTaskbarsUpdated;
        _taskbars.DevicesChanged += _wireless.DevicesChanged;
        ShellWorkArea.Changed += OnWorkAreaChanged;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _taskbars.Updated -= OnTaskbarsUpdated;
        _taskbars.DevicesChanged -= _wireless.DevicesChanged;
        ShellWorkArea.Changed -= OnWorkAreaChanged;
        CloseDragOut();
        foreach (FloatingWidgetWindow window in _floating.Values)
            Close(window);
        _floating.Clear();
        CloseWindow();
        _resources.Dispose();
    }

    /// <summary>Whether the add menu offers <paramref name="kind"/>: most kinds are shown once only.</summary>
    public bool CanAdd(WidgetKind kind) => WidgetView.AllowsSeveral(kind) || Widgets.All(w => w.Kind != kind);

    /// <summary>Adds a widget at the bottom of the sidebar.</summary>
    public void Add(WidgetKind kind)
    {
        if (_window is null || !CanAdd(kind))
            return;

        var widget = new WidgetSettings { Kind = kind };
        Save([.. Widgets, widget]);
        WidgetFrame frame = CreateFrame(CreateView(widget), floating: false);
        _window.Insert(frame, int.MaxValue);
        frame.Loaded += (_, _) => frame.StartBringIntoView();
        Log.Info($"Added the {kind} widget");
    }

    /// <summary>The sidebar's edge was dragged to <paramref name="width"/> effective pixels (placed by the settings change).</summary>
    public void SetWidth(double width) =>
        _settings.Update(_settings.Current with { WidgetSidebarWidth = Math.Clamp(width, SidebarLayout.MinWidth, SidebarLayout.MaxWidth) });

    /// <summary>The sidebar's menu: the Settings window's Widgets page.</summary>
    public void OpenSettings() => _taskbars.OpenSettings(SettingsPage.Widgets);

    private void OnSettingsChanged()
    {
        ShellSettings current = _settings.Current;
        _window?.SetPanelShown(current.ShowWidgetPanel);
        // Its edge dragged, or the Settings window's slider.
        if (_window is not null && _window.Width != current.WidgetSidebarWidth)
            _window.Place(_window.Monitor, current.WidgetSidebarWidth);
        if (current.ShowWidgetSidebar != (_window is not null))
        {
            if (current.ShowWidgetSidebar)
                CreateWindow();
            else
                CloseWindow();
        }

        bool concept = current.Theme == ShellTheme.Concept2088;
        if (current.TaskbarBackdrop != _backdrop || concept != _concept)
        {
            _backdrop = current.TaskbarBackdrop;
            _concept = concept;
            _window?.SetBackdrop(Backdrop);
            _window?.SetTheme(Theme, Accent);
            _window?.SetConcept(_concept);
            foreach (WidgetFrame frame in _window?.Frames ?? [])
                frame.SetConcept(_concept);
            foreach (FloatingWidgetWindow window in _floating.Values)
            {
                window.SetBackdrop(Backdrop);
                window.SetTheme(Theme, Accent);
                window.Frame.SetConcept(_concept);
            }
        }
    }

    // Theme, accent colour, displays, or the taskbars' own space changed.
    private void OnTaskbarsUpdated()
    {
        _window?.SetTheme(Theme, Accent);
        foreach (FloatingWidgetWindow window in _floating.Values)
            window.SetTheme(Theme, Accent);

        if (_window is not null)
        {
            DisplayMonitor? primary = PrimaryMonitor();
            if (primary is null)
                return;
            // On another monitor, or one at another scale: a new window, laid out for it, showing the same views.
            if (primary.Handle != _window.Monitor.Handle || primary.Dpi != _window.Monitor.Dpi || primary.Bounds != _window.Monitor.Bounds)
                CreateWindow(ShutWindow());
            else
            {
                _window.Place(primary, _settings.Current.WidgetSidebarWidth);
            }
        }
    }

    // As the shell, the taskbar or another app's bar took or gave back space (alongside Explorer that comes as a
    // setting change, OnTaskbarsUpdated): the sidebar keeps to the work area.
    private void OnWorkAreaChanged(RectInt32 monitor)
    {
        if (_window is not null && _window.Monitor.Bounds == monitor)
            _window.Place(_window.Monitor, _settings.Current.WidgetSidebarWidth);
    }

    private static DisplayMonitor? PrimaryMonitor() =>
        DisplayMonitor.GetAll() is var monitors && monitors.Count > 0 ? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0] : null;

    /// <param name="views">Views of the docked widgets to show (from the window this one replaces); others are made.</param>
    private void CreateWindow(IReadOnlyList<WidgetView>? views = null)
    {
        if (PrimaryMonitor() is not { } monitor)
        {
            foreach (WidgetView view in views ?? [])
                view.Close();
            return;
        }

        _window = new SidebarWindow(this, _runMode, monitor, _settings.Current.WidgetSidebarWidth, _settings.Current.ShowWidgetPanel, Backdrop, Theme, Accent);
        _window.SetConcept(_concept);
        foreach (WidgetSettings widget in Widgets.Where(w => !w.IsFloating))
        {
            WidgetView view = views?.FirstOrDefault(v => v.Settings.Id == widget.Id) ?? CreateView(widget);
            _window.Insert(CreateFrame(view, floating: false), int.MaxValue);
        }
        _window.AppWindow.Show(activateWindow: false);
    }

    private void CloseWindow()
    {
        foreach (WidgetView view in ShutWindow())
            view.Close();
    }

    // Shuts the sidebar's window, ending a drag out of it, and returns the views it showed.
    private List<WidgetView> ShutWindow()
    {
        if (_window is null)
            return [];

        CloseDragOut();
        List<WidgetView> views = [.. _window.Frames.Select(frame => frame.Release()).OfType<WidgetView>()];
        // A floating widget dragged over the sidebar goes back to its window.
        if (_slot is not null && _floating.TryGetValue(_slot.Widget.Settings.Id, out FloatingWidgetWindow? floating))
        {
            views.Remove(_slot.Widget);
            floating.Frame.Return();
            floating.Conceal(false);
        }
        _slot = null;
        _window.Shut();
        _window = null;
        return views;
    }

    private bool IsDocked(WidgetFrame frame) => _window is not null && _window.Frames.Contains(frame);

    private WidgetView CreateView(WidgetSettings widget)
    {
        WidgetView view = widget.Kind switch
        {
            WidgetKind.Profile => new ProfileWidget(widget),
            WidgetKind.Resources => new ResourcesWidget(widget, _resources),
            WidgetKind.Pictures => new PicturesWidget(widget),
            WidgetKind.Media => new MediaWidget(widget),
            WidgetKind.Weather => new WeatherWidget(widget),
            WidgetKind.Wireless => new WirelessWidget(widget, _wireless),
            WidgetKind.Windows => new WindowsWidget(widget),
            WidgetKind.Clock => new ClockWidget(widget),
            WidgetKind.Calendar => new CalendarWidget(widget),
            WidgetKind.Puzzle => new PuzzleWidget(widget),
            WidgetKind.Currency => new CurrencyWidget(widget),
            WidgetKind.Stocks => new StocksWidget(widget),
            _ => new NotesWidget(widget),
        };
        // The widget's copy may have an old position and size: it was made before the widget was last dragged or resized.
        view.SettingsChanged += changed =>
        {
            if (Widgets.FirstOrDefault(w => w.Id == changed.Id) is { } stored)
            {
                Save(SidebarLayout.Replace(Widgets, changed with
                {
                    X = stored.X,
                    Y = stored.Y,
                    FloatingWidth = stored.FloatingWidth,
                    ContentHeight = stored.ContentHeight,
                }));
            }
        };
        return view;
    }

    private WidgetFrame CreateFrame(WidgetView view, bool floating)
    {
        var frame = new WidgetFrame(view, floating);
        frame.SetConcept(_concept);
        frame.CloseRequested += Remove;
        frame.DragStarted += (_, grab) => _grab = grab;
        if (floating)
        {
            frame.DragMoved += MoveFloating;
            frame.DragEnded += DropFloating;
        }
        else
        {
            frame.DragMoved += MoveDocked;
            frame.DragEnded += DropDocked;
        }
        return frame;
    }

    private void ShowFloating(WidgetSettings widget, PointInt32 topLeft)
    {
        FloatingWidgetWindow window = NewFloatingWindow(widget, CreateView(widget), topLeft);
        window.AppWindow.Show(activateWindow: false);
        _floating[widget.Id] = window;
    }

    private FloatingWidgetWindow NewFloatingWindow(WidgetSettings widget, WidgetView view, PointInt32 topLeft)
    {
        var window = new FloatingWidgetWindow(
            CreateFrame(view, floating: true),
            widget.FloatingWidth ?? SidebarLayout.FloatingWidth,
            topLeft,
            Backdrop,
            Theme,
            Accent);
        window.Resized += (width, contentHeight) =>
        {
            if (Widgets.FirstOrDefault(w => w.Id == widget.Id) is { } stored)
                Save(SidebarLayout.Replace(Widgets, stored with { FloatingWidth = width, ContentHeight = contentHeight }));
        };
        return window;
    }

    private void Remove(WidgetFrame frame)
    {
        string id = frame.Widget.Settings.Id;
        if (_floating.Remove(id, out FloatingWidgetWindow? window))
        {
            Close(window);
        }
        else
        {
            frame.Widget.Close();
            _window?.Remove(frame);
        }
        Save([.. Widgets.Where(w => w.Id != id)]);
        Log.Info($"Closed the {frame.Widget.Settings.Kind} widget");
    }

    // Where a widget held at _grab goes with the pointer at cursor; the grab is kept on the narrower floating widget.
    private static PointInt32 TopLeftAt(PointInt32 cursor, Point grab, double scale) => new(
        cursor.X - (int)Math.Round(Math.Min(grab.X, SidebarLayout.FloatingWidth - 16) * scale),
        cursor.Y - (int)Math.Round(grab.Y * scale));

    private bool IsOverSidebar(PointInt32 cursor) => _window is not null && SidebarLayout.Contains(_window.ScreenBounds, cursor);

    // Over the sidebar a widget being dragged shows itself where it would go, and the others make room (the drop slot):
    // a docked one in its own card, moved among the others, and one from the desktop in a new card, its view moved there
    // from its window. Its window, concealed, keeps the pointer meanwhile. Off the sidebar it shows in its own window,
    // which follows the pointer; a docked one's view moves to a new window then, its card staying behind, lifted and
    // empty, holding the pointer.
    private void MoveDocked(WidgetFrame frame, PointInt32 cursor)
    {
        if (!IsDocked(frame))
            return;
        if (IsOverSidebar(cursor))
        {
            // Back from the desktop.
            if (_dragOut?.Frame.Release() is not null)
            {
                _dragOut.Conceal(true);
                frame.Return();
                SidebarWindow.Lift(frame, false);
            }
            _window!.ShowDropSlot(cursor, frame);
            return;
        }

        PointInt32 topLeft = TopLeftAt(cursor, _grab, frame.XamlRoot.RasterizationScale);
        _dragOut?.MoveTo(topLeft);
        if (frame.Release() is not { } view)
            return;
        SidebarWindow.Lift(frame, true);
        _window!.EndDropSlot();
        if (_dragOut is null)
        {
            _dragOut = NewFloatingWindow(Widgets.Single(w => w.Id == view.Settings.Id), view, topLeft);
            _dragOut.AppWindow.Show(activateWindow: false);
        }
        else
        {
            _dragOut.Frame.Return();
            _dragOut.Conceal(false);
        }
    }

    private void DropDocked(WidgetFrame frame, PointInt32 cursor)
    {
        // The sidebar's window was replaced or closed during the drag (ShutWindow ended it).
        if (!IsDocked(frame))
            return;

        MoveDocked(frame, cursor);
        string id = frame.Widget.Settings.Id;
        _window!.EndDropSlot();
        if (frame.HoldsWidget || _dragOut is not { } window)
        {
            // Let go over the sidebar: its card is where it would go already, and stays there.
            CloseDragOut();
            Save(SidebarLayout.Dock(Widgets, id, _window.DropSlotIndex));
            return;
        }

        // Out on the desktop: the window that followed the pointer stays there, with the view.
        _dragOut = null;
        _floating[id] = window;
        // Once the press that the lifted card holds has ended.
        _dispatcher.Post(() => _window?.Remove(frame));
        SaveFloating(id, window.TopLeft);
    }

    private void MoveFloating(WidgetFrame frame, PointInt32 cursor)
    {
        if (_floating.GetValueOrDefault(frame.Widget.Settings.Id) is not { } window)
            return;

        window.MoveTo(TopLeftAt(cursor, _grab, frame.XamlRoot.RasterizationScale));
        if (IsOverSidebar(cursor))
        {
            if (_slot is null && frame.Release() is { } view)
            {
                _slot = CreateFrame(view, floating: false);
                window.Conceal(true);
            }
            if (_slot is not null)
                _window!.ShowDropSlot(cursor, _slot);
        }
        else if (_slot?.Release() is not null)
        {
            _window?.HideDropSlot();
            _slot = null;
            frame.Return();
            window.Conceal(false);
        }
    }

    private void DropFloating(WidgetFrame frame, PointInt32 cursor)
    {
        string id = frame.Widget.Settings.Id;
        if (!_floating.TryGetValue(id, out FloatingWidgetWindow? window))
            return;

        MoveFloating(frame, cursor);
        if (_slot is null || _window is null)
        {
            SaveFloating(id, window.TopLeft);
            return;
        }

        // Back into the sidebar: it's there already, in its new card, which stays; its window, concealed and empty,
        // goes once the press it holds has ended.
        _window.EndDropSlot();
        _slot = null;
        _floating.Remove(id);
        Save(SidebarLayout.Dock(Widgets, id, _window.DropSlotIndex));
        _dispatcher.Post(() => Close(window));
    }

    private void SaveFloating(string id, PointInt32 topLeft)
    {
        if (Widgets.FirstOrDefault(w => w.Id == id) is { } widget)
            Save(SidebarLayout.Replace(Widgets, widget with { X = topLeft.X, Y = topLeft.Y }));
    }

    private void Save(IReadOnlyList<WidgetSettings> widgets) => _settings.Update(_settings.Current with { Widgets = widgets });

    private static PointInt32 KeepOnScreen(PointInt32 topLeft, IReadOnlyList<DisplayMonitor> monitors)
    {
        DisplayMonitor? primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        if (primary is null)
            return topLeft;

        double scale = primary.Dpi / 96.0;
        return SidebarLayout.KeepOnScreen(
            topLeft,
            (int)Math.Round(SidebarLayout.FloatingWidth * scale),
            [.. monitors.Select(m => m.WorkArea)],
            primary.WorkArea,
            (int)Math.Round(ScreenMargin * scale));
    }

    private void CloseDragOut()
    {
        if (_dragOut is not null)
            Close(_dragOut);
        _dragOut = null;
    }

    private static void Close(FloatingWidgetWindow window)
    {
        window.Frame.Release()?.Close();
        window.Shut();
    }
}
