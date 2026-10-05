using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using AppBar = NeoShell.Interop.Windowing.AppBar;

namespace NeoShell.Widgets;

/// <summary>
/// The widget sidebar: a strip along the right of the primary monitor, from the top to the taskbar, with the
/// taskbar's backdrop. It reserves its space, so maximized windows stop at its edge, and sits just above the desktop.
/// </summary>
internal sealed partial class SidebarWindow : Window
{
    private readonly Sidebar _owner;
    private readonly nint _hwnd;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly AppBar? _appBar;
    private ShellBackdrop _backdrop;
    private DisplayMonitor _monitor;
    private double _width;
    private ElementTheme _theme;
    private Color? _accent;
    // The right edge, in screen pixels, while the left one is dragged.
    private int? _resizeRight;

    public SidebarWindow(Sidebar owner, RunMode runMode, DisplayMonitor monitor, double width, Backdrop backdrop, ElementTheme theme, Color? accent)
    {
        _owner = owner;
        _monitor = monitor;
        InitializeComponent();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // Out of Alt+Tab, but it takes the focus when clicked: notes are typed into.
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        // Alt+F4 while it has the focus; the taskbar's menu hides it.
        AppWindow.Closing += (_, e) => e.Cancel = true;
        _frameless = new FramelessWindow(_hwnd);
        // Stays while peeking at the desktop, as gadgets did.
        Peek.Exclude(_hwnd);
        _backdrop = new ShellBackdrop(backdrop);
        SystemBackdrop = _backdrop;
        WindowTransparency.SetSeeThrough(_hwnd, _backdrop.Kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(theme, accent);

        if (runMode == RunMode.AlongsideExplorer)
        {
            // Explorer manages the screen space: it puts the sidebar left of any app bar already on the right.
            _appBar = new AppBar(_hwnd);
            _appBar.PositionChanged += () => Place(DisplayMonitor.GetAll().FirstOrDefault(m => m.Handle == _monitor.Handle) ?? _monitor, _width);
        }
        _placement = new PinnedWindow(_hwnd, default, PinnedLayer.Desktop);
        Place(monitor, width);

        Closed += (_, _) =>
        {
            // Give the space back first, so windows can use it straight away.
            if (_appBar is not null)
                _appBar.Dispose();
            else
                ShellWorkArea.ReserveRight(_monitor.Bounds, 0);
            _placement.Dispose();
            _frameless.Dispose();
        };
    }

    public DisplayMonitor Monitor => _monitor;

    public RectInt32 ScreenBounds => _placement.Bounds;

    public IEnumerable<WidgetFrame> Frames => Cards.Children.Cast<WidgetFrame>();

    /// <summary>Docks the sidebar on <paramref name="monitor"/>, <paramref name="width"/> effective pixels wide.</summary>
    public void Place(DisplayMonitor monitor, double width)
    {
        _monitor = monitor;
        _width = width;
        int physicalWidth = SidebarLayout.PhysicalWidth(width, monitor.Dpi);
        if (_appBar is not null)
        {
            // The monitor's whole width, but only the work area's height: the sidebar ends above the taskbar.
            RectInt32 area = monitor.Bounds with { Y = monitor.WorkArea.Y, Height = monitor.WorkArea.Height };
            _placement.Bounds = _appBar.DockRight(area, physicalWidth);
        }
        else
        {
            // Its height from what the taskbar reserved, which Windows may not have taken yet.
            ShellWorkArea.ReserveRight(monitor.Bounds, physicalWidth);
            _placement.Bounds = SidebarLayout.Bounds(monitor.Bounds, ShellWorkArea.Get(monitor.Bounds), physicalWidth);
        }
    }

    public void SetTheme(ElementTheme theme, Color? accent)
    {
        _theme = theme;
        _accent = accent;
        Root.RequestedTheme = accent is { } color ? SystemTheme.ThemeOn(color) : theme;
        _backdrop.Theme = Root.RequestedTheme;
        _backdrop.Tint = accent;
        AddMenu.SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = Root.RequestedTheme };
    }

    public void SetBackdrop(Backdrop kind)
    {
        if (kind == _backdrop.Kind)
            return;

        SystemBackdrop = _backdrop = new ShellBackdrop(kind);
        WindowTransparency.SetSeeThrough(_hwnd, kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(_theme, _accent);
    }

    public void Insert(WidgetFrame frame, int index) => Cards.Children.Insert(Math.Clamp(index, 0, Cards.Children.Count), frame);

    public void Remove(WidgetFrame frame) => Cards.Children.Remove(frame);

    /// <summary>Where among the cards a widget dropped at <paramref name="screen"/> goes, leaving <paramref name="except"/> out.</summary>
    public int DropIndex(PointInt32 screen, WidgetFrame? except)
    {
        double y = (screen.Y - _placement.Bounds.Y) / Root.XamlRoot.RasterizationScale;
        List<double> middles = [];
        foreach (WidgetFrame frame in Frames)
        {
            if (frame != except)
                middles.Add(frame.TransformToVisual(Root).TransformPoint(default(Point)).Y + frame.ActualHeight / 2);
        }
        return SidebarLayout.DropIndex(middles, y);
    }

    private void AddMenu_Opening(object sender, object e)
    {
        AddMenu.Items.Clear();
        foreach (WidgetKind kind in Enum.GetValues<WidgetKind>())
        {
            var item = new MenuFlyoutItem
            {
                Text = WidgetView.Title(kind),
                Icon = new FontIcon { Glyph = WidgetView.Glyph(kind) },
                IsEnabled = _owner.CanAdd(kind),
            };
            AutomationProperties.SetAutomationId(item, $"Add{kind}WidgetMenuItem");
            item.Click += (_, _) => _owner.Add(kind);
            AddMenu.Items.Add(item);
        }
    }

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _resizeRight = _placement.Bounds.X + _placement.Bounds.Width;
        Grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    // Only the window follows the pointer; the space is reserved again once it's let go, so windows aren't
    // rearranged on every move.
    private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeRight is not { } right)
            return;

        int width = Math.Clamp(
            right - Cursor.Position().X,
            SidebarLayout.PhysicalWidth(SidebarLayout.MinWidth, _monitor.Dpi),
            SidebarLayout.PhysicalWidth(SidebarLayout.MaxWidth, _monitor.Dpi));
        _placement.Bounds = _placement.Bounds with { X = right - width, Width = width };
    }

    private void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize();
        Grip.ReleasePointerCapture(e.Pointer);
    }

    private void Grip_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndResize();

    private void EndResize()
    {
        if (_resizeRight is null)
            return;

        _resizeRight = null;
        _owner.SetWidth(_placement.Bounds.Width * 96.0 / _monitor.Dpi);
    }
}
