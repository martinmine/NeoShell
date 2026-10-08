using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Snap;

/// <summary>
/// Where a dragged window will snap, as Windows 11 shows it: a frosted, rounded outline of the zone, a little inside
/// it, just behind the window being dragged.
/// </summary>
internal sealed class SnapPreview : Window
{
    // Effective pixels between the zone's edges and the outline.
    private const double Inset = 8;

    private readonly Border _outline = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
    private readonly ShellBackdrop _backdrop = new(Backdrop.Acrylic);
    private readonly nint _hwnd;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private bool _shown;

    public SnapPreview()
    {
        Content = _outline;
        SystemBackdrop = _backdrop;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        nint hwnd = _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        _frameless = new FramelessWindow(hwnd, roundedCorners: true);
        _placement = new PinnedWindow(hwnd, default, PinnedLayer.Normal);

    }

    /// <summary>Shows the zone (screen pixels) just behind <paramref name="dragged"/>.</summary>
    public void Show(RectInt32 zone, uint dpi, nint dragged, ElementTheme theme)
    {
        _outline.RequestedTheme = theme;
        _backdrop.Theme = theme;
        bool dark = theme == ElementTheme.Dark;
        _outline.Background = new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        _outline.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0, 0, 0));

        int inset = (int)Math.Round(Inset * dpi / 96.0);
        _placement.Bounds = new RectInt32(zone.X + inset, zone.Y + inset, zone.Width - 2 * inset, zone.Height - 2 * inset);
        _placement.SetLayer(PinnedLayer.Normal, above: dragged);
        if (!_shown)
        {
            _shown = true;
            AppWindow.Show(activateWindow: false);
            _placement.SetLayer(PinnedLayer.Normal, above: dragged);
        }
    }

    /// <summary>Closes it, letting go of the window first: WinUI can crash handling a move of a window it's tearing down.</summary>
    public void Shut()
    {
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(_hwnd);
        Close();
    }

    public void Hide()
    {
        if (!_shown)
            return;
        _shown = false;
        AppWindow.Hide();
    }
}
