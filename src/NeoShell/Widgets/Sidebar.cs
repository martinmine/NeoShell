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
    // A docked widget dragged off the sidebar: its window on the desktop, shown while the pointer is off the sidebar.
    private FloatingWidgetWindow? _dragOut;
    private Point _grab;
    // The height of the docked widget being dragged, for the gap it leaves where it would go.
    private double _dragHeight;

    public Sidebar(RunMode runMode, SettingsStore settings, Taskbars taskbars)
    {
        _runMode = runMode;
        _settings = settings;
        _taskbars = taskbars;
        _backdrop = settings.Current.TaskbarBackdrop;
    }

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
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _taskbars.Updated -= OnTaskbarsUpdated;
        _taskbars.DevicesChanged -= _wireless.DevicesChanged;
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
        WidgetFrame frame = CreateFrame(widget, floating: false);
        _window.Insert(frame, int.MaxValue);
        frame.Loaded += (_, _) => frame.StartBringIntoView();
        Log.Info($"Added the {kind} widget");
    }

    /// <summary>The sidebar's edge was dragged to <paramref name="width"/> effective pixels.</summary>
    public void SetWidth(double width)
    {
        width = Math.Clamp(width, SidebarLayout.MinWidth, SidebarLayout.MaxWidth);
        _settings.Update(_settings.Current with { WidgetSidebarWidth = width });
        _window?.Place(_window.Monitor, width);
    }

    /// <summary>The sidebar's menu turned its own backdrop behind the widgets on or off.</summary>
    public void SetPanelShown(bool shown) => _settings.Update(_settings.Current with { ShowWidgetPanel = shown });

    private void OnSettingsChanged()
    {
        ShellSettings current = _settings.Current;
        _window?.SetPanelShown(current.ShowWidgetPanel);
        if (current.ShowWidgetSidebar != (_window is not null))
        {
            if (current.ShowWidgetSidebar)
                CreateWindow();
            else
                CloseWindow();
        }

        if (current.TaskbarBackdrop != _backdrop)
        {
            _backdrop = current.TaskbarBackdrop;
            _window?.SetBackdrop(_backdrop);
            foreach (FloatingWidgetWindow window in _floating.Values)
                window.SetBackdrop(_backdrop);
        }
    }

    // Theme, accent colour, displays, or the taskbars' own space changed.
    private void OnTaskbarsUpdated()
    {
        _window?.SetTheme(_taskbars.Theme, _taskbars.Accent);
        foreach (FloatingWidgetWindow window in _floating.Values)
            window.SetTheme(_taskbars.Theme, _taskbars.Accent);

        if (_window is not null)
        {
            DisplayMonitor? primary = PrimaryMonitor();
            if (primary is null)
                return;
            // On another monitor, or one at another scale: a new window, laid out for it.
            if (primary.Handle != _window.Monitor.Handle || primary.Dpi != _window.Monitor.Dpi || primary.Bounds != _window.Monitor.Bounds)
            {
                CloseWindow();
                CreateWindow();
            }
            else
            {
                _window.Place(primary, _settings.Current.WidgetSidebarWidth);
            }
        }
    }

    private static DisplayMonitor? PrimaryMonitor() =>
        DisplayMonitor.GetAll() is var monitors && monitors.Count > 0 ? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0] : null;

    private void CreateWindow()
    {
        if (PrimaryMonitor() is not { } monitor)
            return;

        _window = new SidebarWindow(this, _runMode, monitor, _settings.Current.WidgetSidebarWidth, _settings.Current.ShowWidgetPanel, _backdrop, _taskbars.Theme, _taskbars.Accent);
        foreach (WidgetSettings widget in Widgets.Where(w => !w.IsFloating))
            _window.Insert(CreateFrame(widget, floating: false), int.MaxValue);
        _window.AppWindow.Show(activateWindow: false);
    }

    private void CloseWindow()
    {
        if (_window is null)
            return;

        foreach (WidgetFrame frame in _window.Frames)
            frame.Widget.Close();
        _window.Shut();
        _window = null;
    }

    private WidgetFrame CreateFrame(WidgetSettings widget, bool floating)
    {
        WidgetView view = widget.Kind switch
        {
            WidgetKind.Profile => new ProfileWidget(widget),
            WidgetKind.Resources => new ResourcesWidget(widget, _resources),
            WidgetKind.Pictures => new PicturesWidget(widget),
            WidgetKind.Media => new MediaWidget(widget),
            WidgetKind.Weather => new WeatherWidget(widget),
            WidgetKind.Wireless => new WirelessWidget(widget, _wireless),
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

        var frame = new WidgetFrame(view, floating);
        frame.CloseRequested += Remove;
        frame.DragStarted += (_, grab) => _grab = grab;
        if (floating)
        {
            frame.DragMoved += MoveFloating;
            frame.DragEnded += DropFloating;
        }
        else
        {
            frame.DragStarted += (dragged, _) =>
            {
                _dragHeight = dragged.ActualHeight;
                SidebarWindow.Lift(dragged, true);
            };
            frame.DragMoved += MoveDocked;
            frame.DragEnded += DropDocked;
        }
        return frame;
    }

    private void ShowFloating(WidgetSettings widget, PointInt32 topLeft)
    {
        FloatingWidgetWindow window = NewFloatingWindow(widget, topLeft);
        window.AppWindow.Show(activateWindow: false);
        _floating[widget.Id] = window;
    }

    private FloatingWidgetWindow NewFloatingWindow(WidgetSettings widget, PointInt32 topLeft)
    {
        var window = new FloatingWidgetWindow(CreateFrame(widget, floating: true), topLeft, _backdrop, _taskbars.Theme, _taskbars.Accent);
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

    private void MoveDocked(WidgetFrame frame, PointInt32 cursor)
    {
        if (IsOverSidebar(cursor))
        {
            _dragOut?.AppWindow.Hide();
            _window!.ShowDropSlot(cursor, _dragHeight, except: frame);
            return;
        }
        _window?.HideDropSlot();

        PointInt32 topLeft = TopLeftAt(cursor, _grab, frame.XamlRoot.RasterizationScale);
        if (_dragOut is null)
        {
            _dragOut = NewFloatingWindow(Widgets.Single(w => w.Id == frame.Widget.Settings.Id), topLeft);
            CoverWithPressSnapshot(_dragOut.Frame, frame);
        }
        _dragOut.MoveTo(topLeft);
        _dragOut.AppWindow.Show(activateWindow: false);
    }

    private void DropDocked(WidgetFrame frame, PointInt32 cursor)
    {
        string id = frame.Widget.Settings.Id;
        if (IsOverSidebar(cursor) || _dragOut is null)
        {
            CloseDragOut();
            if (_window is null)
            {
                SidebarWindow.Lift(frame, false);
                return;
            }
            int index = _window.DropSlotIndex;
            Save(SidebarLayout.Dock(Widgets, id, index));
            // Into the gap once the press that holds it has ended.
            _dispatcher.Post(() =>
            {
                _window?.Remove(frame);
                _window?.HideDropSlot();
                _window?.Insert(frame, index);
            });
            return;
        }

        // Out on the desktop: the window that followed the pointer stays there.
        FloatingWidgetWindow window = _dragOut;
        _dragOut = null;
        _floating[id] = window;
        frame.Widget.Close();
        _dispatcher.Post(() => _window?.Remove(frame));
        _window?.HideDropSlot();
        SaveFloating(id, window.TopLeft);
    }

    private void MoveFloating(WidgetFrame frame, PointInt32 cursor)
    {
        if (_floating.GetValueOrDefault(frame.Widget.Settings.Id) is not { } window)
            return;

        window.MoveTo(TopLeftAt(cursor, _grab, frame.XamlRoot.RasterizationScale));
        // Over the sidebar the widgets make room where it would go.
        if (IsOverSidebar(cursor))
            _window!.ShowDropSlot(cursor, frame.ActualHeight, except: null);
        else
            _window?.HideDropSlot();
    }

    private async void DropFloating(WidgetFrame frame, PointInt32 cursor)
    {
        string id = frame.Widget.Settings.Id;
        if (!_floating.TryGetValue(id, out FloatingWidgetWindow? window))
            return;

        if (!IsOverSidebar(cursor) || _window is null)
        {
            SaveFloating(id, window.TopLeft);
            return;
        }

        // Back into the sidebar: it goes straight into the gap the others made, and takes its place among them. Its new
        // card shows a picture of it until the new view is ready and drawn, and the window goes once the card is drawn:
        // there's no moment without either, nor the widget empty and filling in.
        int index = _window.DropSlotIndex;
        _floating.Remove(id);
        Save(SidebarLayout.Dock(Widgets, id, index));
        window.MoveTo(_window.DropSlotTopLeft);
        WidgetSnapshot? snapshot = await frame.PressSnapshot;
        if (_window is null)
        {
            Close(window);
            return;
        }
        WidgetFrame docked = CreateFrame(Widgets.Single(w => w.Id == id), floating: false);
        if (snapshot is not null)
            docked.Cover(snapshot);
        _window.HideDropSlot();
        _window.Insert(docked, index);
        docked.Loaded += (_, _) => WidgetFrame.AfterFramesDrawn(() => Close(window));
    }

    private void SaveFloating(string id, PointInt32 topLeft)
    {
        if (Widgets.FirstOrDefault(w => w.Id == id) is { } widget)
            Save(SidebarLayout.Replace(Widgets, widget with { X = topLeft.X, Y = topLeft.Y }));
    }

    /// <summary>Covers a new view of a widget with the picture of it taken as it was pressed, once that's taken.</summary>
    private static async void CoverWithPressSnapshot(WidgetFrame frame, WidgetFrame pressed)
    {
        if (await pressed.PressSnapshot is { } snapshot)
            frame.Cover(snapshot);
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
        window.Frame.Widget.Close();
        window.Shut();
    }
}
