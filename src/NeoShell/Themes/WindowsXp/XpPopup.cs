using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// A frameless topmost window showing an XP surface drawn in code (<see cref="XpCanvas"/>), with icons and hit areas over
/// it: the menus, tooltips, Start and the power dialogs. See-through where the canvas is, for XP's drop shadows and
/// rounded corners. The art is XP's 96 DPI pixels, scaled to the monitor.
/// </summary>
internal sealed class XpPopup : Window
{
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly Image _surface = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private WriteableBitmap? _bitmap;
    private bool _shut;

    /// <param name="activates">Whether it takes the keyboard when shown (Start, the dialogs); menus and tooltips don't.</param>
    public XpPopup(bool activates)
    {
        Root = new Grid();
        Root.Children.Add(_surface);
        Root.Children.Add(Overlay);
        Content = Root;
        SystemBackdrop = new ShellBackdrop(Backdrop.Transparent);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(Handle, activates ? ExtendedWindowStyles.ToolWindow : ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        Peek.Exclude(Handle);
        _frameless = new FramelessWindow(Handle);
        _placement = new PinnedWindow(Handle, default, PinnedLayer.Topmost);
        WindowTransparency.SetSeeThrough(Handle, true);
        Activates = activates;
    }

    public nint Handle { get; }

    public bool Activates { get; }

    public Grid Root { get; }

    /// <summary>Icons and hit areas, in the art's pixels.</summary>
    public Canvas Overlay { get; } = new();

    public bool IsShown { get; private set; }

    /// <summary>Where it is on screen, in physical pixels.</summary>
    public RectInt32 ScreenBounds => _placement.Bounds;

    /// <summary>Shows what's drawn, the window's size following the canvas.</summary>
    public void Draw(XpCanvas canvas)
    {
        if (_bitmap is null || _bitmap.PixelWidth != canvas.Width || _bitmap.PixelHeight != canvas.Height)
        {
            _bitmap = new WriteableBitmap(canvas.Width, canvas.Height);
            _surface.Source = _bitmap;
            _surface.Width = Overlay.Width = canvas.Width;
            _surface.Height = Overlay.Height = canvas.Height;
        }
        using (Stream stream = _bitmap.PixelBuffer.AsStream())
            stream.Write(MemoryMarshal.AsBytes(canvas.Pixels.AsSpan()));
        _bitmap.Invalidate();
    }

    /// <summary>Shows it at a place on screen (physical pixels), its size the canvas's at the monitor's scale.</summary>
    public void ShowAt(PointInt32 topLeft, double scale)
    {
        if (_bitmap is null || _shut)
            return;

        _placement.Bounds = new RectInt32(topLeft.X, topLeft.Y,
            (int)Math.Ceiling(_bitmap.PixelWidth * scale), (int)Math.Ceiling(_bitmap.PixelHeight * scale));
        _placement.SetLayer(PinnedLayer.Topmost);
        IsShown = true;
        AppWindow.Show(activateWindow: Activates);
        if (Activates)
        {
            TopLevelWindows.Activate(Handle);
            Activate();
        }
    }

    public void Hide()
    {
        if (!IsShown || _shut)
            return;

        IsShown = false;
        AppWindow.Hide();
    }

    /// <summary>Closes it for good, letting go of the window first (see <see cref="WindowClosing.IgnoreMoves"/>).</summary>
    public void Shut()
    {
        if (_shut)
            return;

        _shut = true;
        IsShown = false;
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(Handle);
        Close();
    }

    /// <summary>A point on screen in the art's pixels.</summary>
    public Windows.Foundation.Point FromScreen(PointInt32 point, double scale) =>
        new((point.X - _placement.Bounds.X) / scale, (point.Y - _placement.Bounds.Y) / scale);
}
