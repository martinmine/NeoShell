using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private bool _panelShown = true;
    // The cards the window is cut to while the panel is hidden, in pixels; null shows all of it.
    private List<RectInt32>? _region;

    /// <param name="panelShown">The sidebar's own backdrop behind the widgets; otherwise only the widgets show.</param>
    public SidebarWindow(Sidebar owner, RunMode runMode, DisplayMonitor monitor, double width, bool panelShown, Backdrop backdrop, ElementTheme theme, Color? accent)
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
        SetPanelShown(panelShown);
        // The cards move with layout (a widget grows, one is added) and with scrolling.
        Root.LayoutUpdated += (_, _) => UpdateRegion();
        Scroller.ViewChanged += (_, _) => UpdateRegion();

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
        SidebarMenu.SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = Root.RequestedTheme };
    }

    /// <summary>
    /// Shows the sidebar's own backdrop behind the widgets, or only the widgets: the window is then cut to its cards,
    /// each keeping the backdrop behind it, and the rest of it is as if it weren't there.
    /// </summary>
    public void SetPanelShown(bool shown)
    {
        _panelShown = shown;
        // Widgets are added from the sidebar's menu then.
        Header.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        UpdateRegion();
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

    private void UpdateRegion()
    {
        List<RectInt32>? rects = null;
        if (!_panelShown && Root.XamlRoot is { } root)
        {
            double scale = root.RasterizationScale;
            Rect viewport = Scroller.TransformToVisual(Root).TransformBounds(new Rect(0, 0, Scroller.ActualWidth, Scroller.ActualHeight));
            rects = [];
            foreach (WidgetFrame frame in Frames)
            {
                // Only what's scrolled into view.
                Rect card = frame.TransformToVisual(Root).TransformBounds(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight));
                double top = Math.Max(card.Top, viewport.Top);
                double bottom = Math.Min(card.Bottom, viewport.Bottom);
                if (bottom <= top || card.Width <= 0)
                    continue;
                rects.Add(new RectInt32(
                    (int)Math.Round(card.X * scale),
                    (int)Math.Round(top * scale),
                    (int)Math.Round(card.Width * scale),
                    (int)Math.Round((bottom - top) * scale)));
            }
        }

        if (rects is null ? _region is null : _region is not null && rects.SequenceEqual(_region))
            return;
        _region = rects;
        // Rounded as the cards are (8 epx).
        WindowRegion.SetRoundedRects(_hwnd, rects, (int)Math.Round(8 * (Root.XamlRoot?.RasterizationScale ?? 1)));
    }

    private void Root_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.TryGetPosition(Root, out Point point))
            SidebarMenu.ShowAt(Root, new FlyoutShowOptions { Position = point });
        else
            SidebarMenu.ShowAt(Root);
    }

    private void SidebarMenu_Opening(object sender, object e)
    {
        ShowPanelItem.IsChecked = _panelShown;
        FillAddMenu(AddSubMenu.Items);
    }

    private void ShowPanelItem_Click(object sender, RoutedEventArgs e) => _owner.SetPanelShown(ShowPanelItem.IsChecked);

    private void AddMenu_Opening(object sender, object e) => FillAddMenu(AddMenu.Items);

    private void FillAddMenu(IList<MenuFlyoutItemBase> items)
    {
        items.Clear();
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
            items.Add(item);
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
