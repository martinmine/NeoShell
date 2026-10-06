using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.Themes;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Widgets;

/// <summary>
/// A widget dragged out of the sidebar onto the desktop: a rounded window of its own with the taskbar's backdrop,
/// as tall as the widget, just above the desktop.
/// </summary>
internal sealed class FloatingWidgetWindow : Window
{
    private readonly nint _hwnd;
    private readonly Grid _root;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private ShellBackdrop _backdrop;
    private ElementTheme _theme;
    private Color? _accent;
    // Where the corner was pressed, and the size then, while the widget is being resized.
    private (PointInt32 Start, double Width, double ContentHeight)? _resize;

    public FloatingWidgetWindow(WidgetFrame frame, PointInt32 topLeft, Backdrop backdrop, ElementTheme theme, Color? accent)
    {
        Frame = frame;
        frame.Width = frame.Widget.Settings.FloatingWidth ?? SidebarLayout.FloatingWidth;
        // Its own height, not the window's: the window follows it, also when it gets smaller.
        frame.VerticalAlignment = VerticalAlignment.Top;
        // A scroll viewer that doesn't scroll lets the widget take the height it wants; the window then follows (Resize).
        _root = new Grid
        {
            Children =
            {
                new ScrollViewer
                {
                    Content = frame,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    VerticalScrollMode = ScrollMode.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            },
        };
        Content = _root;
        if (frame.Widget.CanResize)
            AddResizeGrip();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        AppWindow.Closing += (_, e) => e.Cancel = true;
        _frameless = new FramelessWindow(_hwnd, roundedCorners: ShellTheme.Current.RoundedCorners);
        Peek.Exclude(_hwnd);
        _backdrop = new ShellBackdrop(backdrop);
        SystemBackdrop = _backdrop;
        WindowTransparency.SetSeeThrough(_hwnd, _backdrop.Kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(theme, accent);

        // Sized for the monitor it's on until the widget has been laid out.
        uint dpi = DisplayMonitor.GetAll().FirstOrDefault(m => SidebarLayout.Contains(m.Bounds, topLeft))?.Dpi ?? 96;
        int width = (int)Math.Round(frame.Width * dpi / 96.0);
        _placement = new PinnedWindow(_hwnd, new RectInt32(topLeft.X, topLeft.Y, width, width / 2), PinnedLayer.Desktop);

        // After the layout pass that changed it.
        frame.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(Resize);
        _root.Loaded += (_, _) => _root.XamlRoot.Changed += (_, _) => Resize();
    }

    /// <summary>Closes the window (see <see cref="WindowClosing.IgnoreMoves"/>).</summary>
    public void Shut()
    {
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(_hwnd);
        Close();
    }

    public WidgetFrame Frame { get; }

    public RectInt32 ScreenBounds => _placement.Bounds;

    public PointInt32 TopLeft => new(_placement.Bounds.X, _placement.Bounds.Y);

    /// <summary>The widget was resized by its corner: its width and <see cref="WidgetView.ContentHeight"/>, in effective pixels.</summary>
    public event Action<double, double>? Resized;

    public void MoveTo(PointInt32 topLeft) => _placement.Bounds = _placement.Bounds with { X = topLeft.X, Y = topLeft.Y };

    public void SetTheme(ElementTheme theme, Color? accent)
    {
        _theme = theme;
        _accent = accent;
        _root.RequestedTheme = accent is { } color ? SystemTheme.ThemeOn(color) : theme;
        _backdrop.Theme = _root.RequestedTheme;
        _backdrop.Tint = accent;
    }

    public void SetBackdrop(Backdrop kind)
    {
        if (kind == _backdrop.Kind)
            return;

        SystemBackdrop = _backdrop = new ShellBackdrop(kind);
        WindowTransparency.SetSeeThrough(_hwnd, kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(_theme, _accent);
    }

    // The window follows the widget's size, and its scale on another monitor.
    private void Resize()
    {
        if (_root.XamlRoot is not { } root || Frame.ActualHeight <= 0)
            return;

        double scale = root.RasterizationScale;
        _placement.Bounds = _placement.Bounds with
        {
            Width = (int)Math.Round(Frame.ActualWidth * scale),
            Height = (int)Math.Round(Frame.ActualHeight * scale),
        };
        // Through the app window too: WinUI's window keeps the size it last knew of otherwise, and puts it back.
        AppWindow.Resize(new SizeInt32(_placement.Bounds.Width, _placement.Bounds.Height));
    }

    // A corner to drag at the bottom right; the window follows the widget's new size.
    private void AddResizeGrip()
    {
        const double size = 16;
        // The window is the widget's size, so its corner is the widget's.
        var grip = new CornerGrip
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetAutomationId(grip, "WidgetResizeGrip");
        _root.Children.Add(grip);

        grip.PointerPressed += (_, e) =>
        {
            _resize = (Cursor.Position(), Frame.ActualWidth, Frame.Widget.ContentHeight);
            grip.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        grip.PointerMoved += (_, _) =>
        {
            if (_resize is not { } resize)
                return;
            double scale = _root.XamlRoot.RasterizationScale;
            PointInt32 cursor = Cursor.Position();
            Frame.Width = Math.Clamp(resize.Width + (cursor.X - resize.Start.X) / scale, SidebarLayout.FloatingMinWidth, SidebarLayout.FloatingMaxWidth);
            Frame.Widget.ContentHeight = Math.Clamp(resize.ContentHeight + (cursor.Y - resize.Start.Y) / scale,
                SidebarLayout.MinContentHeight, SidebarLayout.MaxContentHeight);
        };
        void EndResize()
        {
            if (_resize is null)
                return;
            _resize = null;
            Resized?.Invoke(Frame.Width, Frame.Widget.ContentHeight);
        }
        grip.PointerReleased += (_, e) =>
        {
            EndResize();
            grip.ReleasePointerCapture(e.Pointer);
        };
        grip.PointerCaptureLost += (_, _) => EndResize();
    }
}
