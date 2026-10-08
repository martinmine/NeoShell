using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// The Snap layouts bar at the top of the screen while a window is dragged, as Explorer's: as the drag starts it peeks
/// 10 effective pixels down from the top centre; with the pointer within 12 of the top over it, it comes down to 25 from
/// the top with the layouts in a row; the zone under the pointer is where the window goes if it's let go there. The
/// app's move loop has the mouse, so the pointer is followed from <see cref="WindowSnapping"/>'s polling.
/// </summary>
internal sealed class SnapBar : Window
{
    // Effective pixels.
    private const double Peeking = 10;
    private const double Reach = 12;
    private const double Top = 25;

    private readonly DisplayMonitor _monitor;
    private readonly SnapLayoutPicker _picker;
    private readonly nint _hwnd;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly WindowSlide _slide;
    private readonly RectInt32 _expanded;
    private readonly RectInt32 _peeking;
    private bool _closing;
    private bool _placed;

    public SnapBar(DisplayMonitor monitor, IReadOnlyList<SnapChoice> choices, IReadOnlyList<ImageSource?> icons, ElementTheme theme)
    {
        _monitor = monitor;
        Title = "Snap bar";
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = theme, Tint = SnapLayoutPicker.BackdropTint(theme), TintOpacities = SnapLayoutPicker.BackdropOpacities };

        nint hwnd = _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        _frameless = new FramelessWindow(hwnd, roundedCorners: true);
        _picker = new SnapLayoutPicker(choices, columns: choices.Count, SnapLayoutPicker.BarPadding, theme, icons, numbers: false);
        Content = _picker.Root;

        double scale = monitor.Dpi / 96.0;
        int width = (int)Math.Ceiling(_picker.Size.Width * scale);
        int height = (int)Math.Ceiling(_picker.Size.Height * scale);
        RectInt32 area = monitor.WorkArea;
        int x = area.X + (area.Width - width) / 2;
        _expanded = new RectInt32(x, area.Y + (int)Math.Round(Top * scale), width, height);
        _peeking = _expanded with { Y = area.Y + (int)Math.Round(Peeking * scale) - height };
        _placement = new PinnedWindow(hwnd, _peeking with { X = FirstFrame.OffScreen }, PinnedLayer.Topmost);
        _slide = new WindowSlide(_placement);
        Closed += (_, _) => _slide.Stop();
    }

    public DisplayMonitor Monitor => _monitor;

    public IReadOnlyList<SnapChoice> Choices => _picker.Choices;

    public bool IsExpanded { get; private set; }

    /// <summary>Slides in to peek from the top, as a drag starts (once drawn).</summary>
    public void Peek()
    {
        AppWindow.Show(activateWindow: false);
        FirstFrame.After(_picker.Root, () =>
        {
            if (_closing)
                return;
            _placed = true;
            _placement.Bounds = _peeking with { Y = _monitor.WorkArea.Y - _peeking.Height };
            _slide.To(_peeking, TimeSpan.FromMilliseconds(70), decelerate: true);
        });
    }

    /// <summary>
    /// Follows the dragged window's pointer (screen pixels): comes down when it reaches the top over the bar, goes back
    /// up when it leaves; returns the zone under it while down (the layout's and the zone's index).
    /// </summary>
    public (int Choice, int Zone)? Follow(PointInt32 pointer)
    {
        double scale = _monitor.Dpi / 96.0;
        if (!_placed)
            return null;
        if (!IsExpanded)
        {
            bool reached = pointer.X >= _expanded.X && pointer.X < _expanded.X + _expanded.Width
                && pointer.Y < _monitor.WorkArea.Y + Reach * scale && pointer.Y >= _monitor.Bounds.Y;
            if (!reached)
                return null;
            IsExpanded = true;
            _slide.To(_expanded, TimeSpan.FromMilliseconds(180), decelerate: true);
        }
        else if (pointer.X < _expanded.X || pointer.X >= _expanded.X + _expanded.Width || pointer.Y >= _expanded.Y + _expanded.Height)
        {
            // Away from it; above it (pushed against the top) it stays.
            IsExpanded = false;
            _picker.Highlight(null);
            _slide.To(_peeking, TimeSpan.FromMilliseconds(200), decelerate: true);
            return null;
        }

        RectInt32 bounds = _placement.Bounds;
        (int Choice, int Zone)? zone = _picker.ZoneAt(new Point((pointer.X - bounds.X) / scale, (pointer.Y - bounds.Y) / scale));
        _picker.Highlight(zone);
        return zone;
    }

    /// <summary>Slides back up out of the screen and closes.</summary>
    public void Leave()
    {
        if (_closing)
            return;
        _closing = true;
        _picker.Highlight(null);
        _slide.To(_peeking with { Y = _monitor.WorkArea.Y - _peeking.Height }, TimeSpan.FromMilliseconds(200), decelerate: false, Shut);
    }

    /// <summary>Closes at once.</summary>
    public void Shut()
    {
        _closing = true;
        _slide.Stop();
        // Let go of the window first: WinUI can crash handling a move of a window it's tearing down.
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(_hwnd);
        Close();
    }

    private static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
}
