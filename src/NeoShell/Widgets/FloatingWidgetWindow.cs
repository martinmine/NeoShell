using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
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
    private readonly Canvas _root;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private ShellBackdrop _backdrop;
    private ElementTheme _theme;
    private Color? _accent;

    public FloatingWidgetWindow(WidgetFrame frame, PointInt32 topLeft, Backdrop backdrop, ElementTheme theme, Color? accent)
    {
        Frame = frame;
        frame.Width = SidebarLayout.FloatingWidth;
        // A canvas lets the widget take the height it wants; the window then follows (Resize).
        _root = new Canvas { Children = { frame } };
        Content = _root;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        AppWindow.Closing += (_, e) => e.Cancel = true;
        _frameless = new FramelessWindow(_hwnd, roundedCorners: true);
        Peek.Exclude(_hwnd);
        _backdrop = new ShellBackdrop(backdrop);
        SystemBackdrop = _backdrop;
        WindowTransparency.SetSeeThrough(_hwnd, _backdrop.Kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(theme, accent);

        // Sized for the monitor it's on until the widget has been laid out.
        uint dpi = DisplayMonitor.GetAll().FirstOrDefault(m => SidebarLayout.Contains(m.Bounds, topLeft))?.Dpi ?? 96;
        int width = (int)Math.Round(SidebarLayout.FloatingWidth * dpi / 96.0);
        _placement = new PinnedWindow(_hwnd, new RectInt32(topLeft.X, topLeft.Y, width, width / 2), PinnedLayer.Desktop);

        frame.SizeChanged += (_, _) => Resize();
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
            Width = (int)Math.Round(SidebarLayout.FloatingWidth * scale),
            Height = (int)Math.Round(Frame.ActualHeight * scale),
        };
    }
}
