using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Desktop;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Notifications;
using NeoShell.Taskbar;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace NeoShell.Capture;

/// <summary>
/// Win+Shift+S (and Print Screen, see <see cref="PrintScreenKeys"/>) as the shell, as Snipping Tool's overlay does it
/// under Explorer. Snipping Tool can't show its overlay without Explorer (it can't capture a monitor; see design.md,
/// Hotkeys), so NeoShell snips by itself and hands the snip to Snipping Tool's editor, which works as the shell.
/// </summary>
/// <remarks>
/// The screen freezes, dimmed, with the toolbar at the top of the primary monitor. Rectangle: the area dragged out
/// shows at full brightness, outlined. Window: the window under the pointer shows at full brightness; a click takes
/// it. Full screen: picking it takes every monitor at once. Freeform: the path drawn is the snip, flashed in the accent
/// colour as it's taken. Esc, the close button or another window taking the foreground cancels. The mode is
/// Snipping Tool's own setting, remembered after each snip (but full screen).
/// </remarks>
internal sealed class ScreenSnip
{
    private static ScreenSnip? s_current;

    // The whole virtual screen, taken before the overlay covers it, and where it is.
    private readonly IconBitmap _screen;
    private readonly RectInt32 _screenBounds;
    private readonly IReadOnlyList<SnipTarget> _targets;
    private readonly nint _clipboardOwner;
    private readonly Func<ToastPopups?> _toasts;
    private readonly WindowTracker _tracker;
    private readonly List<SnipWindow> _windows = [];
    private SnipMode _mode;
    private bool _ended;

    /// <param name="clipboardOwner">A window of NeoShell's that outlives the snip, to own the clipboard.</param>
    /// <param name="toasts">Where Snipping Tool's toast for the snip goes.</param>
    /// <param name="tracker">Tells when another window takes the foreground.</param>
    public static void Start(nint clipboardOwner, Func<ToastPopups?> toasts, WindowTracker tracker)
    {
        if (s_current is not null)
            return;

        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        RectInt32 screenBounds = WallpaperLayout.Union(monitors.Select(m => m.Bounds));
        IconBitmap screen;
        try
        {
            screen = ScreenCapture.Capture(screenBounds);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warn("Could not take a screenshot", ex);
            return;
        }
        // Also before the overlay covers them.
        var windows = new List<SnipTarget>();
        foreach (nint hwnd in TopLevelWindows.GetAll())
        {
            bool onScreen = TopLevelWindows.IsOnScreen(hwnd);
            windows.Add(new SnipTarget(hwnd, TopLevelWindows.GetProcessId(hwnd), onScreen, onScreen ? TopLevelWindows.GetVisibleBounds(hwnd) : default));
        }
        int? saved = SnippingToolSettings.ReadSnippingMode();
        SnipMode mode = saved is 1 or 2 or 4 ? (SnipMode)saved : SnipMode.Rectangle;

        s_current = new ScreenSnip(monitors, screen, screenBounds, SnipTargets.Choose(windows, Environment.ProcessId), mode, clipboardOwner, toasts, tracker);
    }

    private ScreenSnip(
        IReadOnlyList<DisplayMonitor> monitors, IconBitmap screen, RectInt32 screenBounds, IReadOnlyList<SnipTarget> targets,
        SnipMode mode, nint clipboardOwner, Func<ToastPopups?> toasts, WindowTracker tracker)
    {
        _screen = screen;
        _screenBounds = screenBounds;
        _targets = targets;
        _mode = mode;
        _clipboardOwner = clipboardOwner;
        _toasts = toasts;
        _tracker = tracker;
        Log.Info($"Snip started, {mode}");

        foreach (DisplayMonitor monitor in monitors)
        {
            var window = new SnipWindow(monitor, this);
            _windows.Add(window);
            if (monitor.IsPrimary)
            {
                // For Esc. WinUI shows the window but leaves the foreground where it was: the keys went to the app
                // in front.
                window.Activate();
                TopLevelWindows.Activate(window.Handle);
            }
            else
            {
                window.AppWindow.Show(activateWindow: false);
            }
        }
        tracker.ForegroundChanged += OnForegroundChanged;
    }

    private SnipMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            foreach (SnipWindow window in _windows)
                window.ShowMode();
            if (value == SnipMode.FullScreen)
                Finish(_screen, value);
        }
    }

    // Window mode: the window under the pointer, shown on every monitor it's on.
    private void Highlight(SnipTarget? target)
    {
        foreach (SnipWindow window in _windows)
            window.Highlight(target);
    }

    private void Cancel()
    {
        if (!_ended)
            Log.Info("Snip cancelled");
        Finish(null, _mode);
    }

    /// <summary>Ends the snip, keeping <paramref name="snip"/> unless it's null (cancelled).</summary>
    private void Finish(IconBitmap? snip, SnipMode mode)
    {
        if (_ended)
            return;
        _ended = true;
        _tracker.ForegroundChanged -= OnForegroundChanged;
        foreach (SnipWindow window in _windows)
        {
            // Off the screen at once, as Snipping Tool's goes: a closing WinUI window shows a black frame first.
            TopLevelWindows.Cloak(window.Handle, true);
            window.Close();
        }
        _windows.Clear();
        s_current = null;
        if (snip is null)
            return;

        if (mode != SnipMode.FullScreen)
            Task.Run(() => SnippingToolSettings.SaveSnippingMode((int)mode));
        Screenshots.KeepSnip(snip, transparent: mode == SnipMode.Freeform, _clipboardOwner, _toasts);
    }

    // Another app took the foreground: Snipping Tool's overlay goes away then too.
    private void OnForegroundChanged()
    {
        nint foreground = TopLevelWindows.GetForeground();
        if (foreground != 0 && TopLevelWindows.GetProcessId(foreground) != Environment.ProcessId)
            Cancel();
    }

    private IconBitmap Crop(RectInt32 area)
    {
        // Screen pixels to the picture's, kept inside it.
        int left = Math.Clamp(area.X - _screenBounds.X, 0, _screen.Width);
        int top = Math.Clamp(area.Y - _screenBounds.Y, 0, _screen.Height);
        int right = Math.Clamp(area.X + area.Width - _screenBounds.X, left, _screen.Width);
        int bottom = Math.Clamp(area.Y + area.Height - _screenBounds.Y, top, _screen.Height);
        var pixels = new byte[(right - left) * (bottom - top) * 4];
        for (int row = 0; row < bottom - top; row++)
            Array.Copy(_screen.Pixels, ((top + row) * _screen.Width + left) * 4, pixels, row * (right - left) * 4, (right - left) * 4);
        return new IconBitmap(right - left, bottom - top, pixels);
    }

    /// <summary>One monitor's frozen picture with the selection, in the topmost band over everything.</summary>
    private sealed class SnipWindow : Window
    {
        // Snipping Tool's dimming: black at 60 %.
        private static readonly Color s_dim = Color.FromArgb(0x99, 0, 0, 0);

        private readonly DisplayMonitor _monitor;
        private readonly ScreenSnip _snip;
        private readonly FramelessWindow _frameless;
        private readonly PinnedWindow _placement;
        private readonly SnipSurface _surface = new();
        private readonly SnipToolbar? _toolbar;
        private readonly RectangleGeometry _all = new();
        private readonly RectangleGeometry _hole = new();
        // Just outside the selection, each side a line of its own as Snipping Tool's: dashed, 4 pixels white and 3 not,
        // the first dash 2 pixels in from the corner.
        private readonly Line[] _outline = [.. Enumerable.Range(0, 4).Select(_ => new Line
        {
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            StrokeDashOffset = 5,
            IsHitTestVisible = false,
        })];
        private readonly Canvas _outlines = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Polyline _stroke = new()
        {
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        private readonly Polygon _flash = new() { IsHitTestVisible = false, Opacity = 0 };
        // The freeform path in the picture's pixels, as drawn.
        private readonly List<Point> _path = [];
        private Point? _start;
        private SnipTarget? _pressed;

        public SnipWindow(DisplayMonitor monitor, ScreenSnip snip)
        {
            _monitor = monitor;
            _snip = snip;
            Title = "Screen snip";

            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            AppWindow.SetPresenter(presenter);
            Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            WindowStyles.AddExtended(Handle, ExtendedWindowStyles.ToolWindow);
            _frameless = new FramelessWindow(Handle);
            _placement = new PinnedWindow(Handle, monitor.Bounds, PinnedLayer.Topmost);

            _flash.Fill = new SolidColorBrush(Application.Current.Resources["SystemAccentColor"] is Color accent ? accent : Colors.DodgerBlue);
            var picture = new Image { Source = AppIcons.ToImageSource(snip.Crop(monitor.Bounds)), Stretch = Stretch.Fill };
            var dim = new Path
            {
                Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { _all, _hole } },
                Fill = new SolidColorBrush(s_dim),
                IsHitTestVisible = false,
            };
            foreach (Line line in _outline)
                _outlines.Children.Add(line);
            var surfaceContent = new Grid { Children = { picture, dim, _outlines, _stroke, _flash } };
            surfaceContent.SizeChanged += (_, e) => _all.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
            _surface.Content = surfaceContent;
            _surface.PointerPressed += Surface_PointerPressed;
            _surface.PointerMoved += Surface_PointerMoved;
            _surface.PointerReleased += Surface_PointerReleased;

            var root = new Grid { Children = { _surface } };
            if (monitor.IsPrimary)
            {
                _toolbar = new SnipToolbar(snip.Mode)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 12, 0, 0),
                };
                _toolbar.ModeChosen += mode => _snip.Mode = mode;
                _toolbar.CloseRequested += _snip.Cancel;
                root.Children.Add(_toolbar);
                // Tab goes round the toolbar's controls only, as in Snipping Tool's.
                _surface.IsTabStop = false;
            }
            root.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Escape)
                {
                    e.Handled = true;
                    _snip.Cancel();
                }
            };
            Content = root;
            ShowMode();

            Activated += (_, e) =>
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated)
                    return;
                if (_toolbar is not null)
                    _toolbar.FocusFirst();
                else
                    _surface.Focus(FocusState.Programmatic);
            };
            Closed += (_, _) =>
            {
                _placement.Dispose();
                _frameless.Dispose();
            };
        }

        public nint Handle { get; }

        // Screen pixels per effective pixel.
        private double Scale => _surface.ActualWidth > 0 ? _monitor.Bounds.Width / _surface.ActualWidth : _monitor.Dpi / 96.0;

        /// <summary>Starts the mode afresh: nothing selected, and the mode's pointer.</summary>
        public void ShowMode()
        {
            _start = null;
            _pressed = null;
            _path.Clear();
            _stroke.Points.Clear();
            ShowHole(Rect.Empty);
            // Snipping Tool's: a crosshair, but the arrow over a window to take or on the freeform ink.
            _surface.SetCursor(_snip._mode == SnipMode.Freeform ? InputSystemCursorShape.Arrow : InputSystemCursorShape.Cross);
        }

        public void Highlight(SnipTarget? target)
        {
            if (target is null)
            {
                ShowHole(Rect.Empty);
                _surface.SetCursor(InputSystemCursorShape.Cross);
                return;
            }
            RectInt32 bounds = target.Bounds;
            double scale = Scale;
            ShowHole(new Rect(
                (bounds.X - _monitor.Bounds.X) / scale, (bounds.Y - _monitor.Bounds.Y) / scale, bounds.Width / scale, bounds.Height / scale));
            _surface.SetCursor(InputSystemCursorShape.Arrow);
        }

        private void ShowHole(Rect rect) => _hole.Rect = rect.IsEmpty ? default : rect;

        private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_surface);
            if (!point.Properties.IsLeftButtonPressed)
                return;
            switch (_snip._mode)
            {
                case SnipMode.Window:
                    _pressed = SnipTargets.At(_snip._targets, ToScreen(point.Position));
                    return;
                case SnipMode.Rectangle:
                    _start = point.Position;
                    Select(new Rect(point.Position, point.Position));
                    break;
                case SnipMode.Freeform:
                    _start = point.Position;
                    _path.Clear();
                    _stroke.Points.Clear();
                    AddToPath(point.Position);
                    break;
                default:
                    return;
            }
            _surface.CapturePointer(e.Pointer);
        }

        private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            Point position = e.GetCurrentPoint(_surface).Position;
            switch (_snip._mode)
            {
                case SnipMode.Window:
                    _snip.Highlight(SnipTargets.At(_snip._targets, ToScreen(position)));
                    break;
                case SnipMode.Rectangle when _start is { } start:
                    Select(new Rect(start, Clamp(position)));
                    break;
                case SnipMode.Freeform when _start is not null:
                    AddToPath(Clamp(position));
                    break;
            }
        }

        private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            Point position = e.GetCurrentPoint(_surface).Position;
            if (_snip._mode == SnipMode.Window)
            {
                SnipTarget? target = SnipTargets.At(_snip._targets, ToScreen(position));
                if (target is not null && target == _pressed)
                    _snip.Finish(_snip.Crop(target.Bounds), SnipMode.Window);
                _pressed = null;
                return;
            }
            if (_start is not { } start)
                return;
            _start = null;
            _surface.ReleasePointerCapture(e.Pointer);

            if (_snip._mode == SnipMode.Rectangle)
                SnipRectangle(new Rect(start, Clamp(position)));
            else
                SnipFreeform();
        }

        private void SnipRectangle(Rect rect)
        {
            PointInt32 topLeft = ToScreen(new Point(rect.X, rect.Y));
            PointInt32 bottomRight = ToScreen(new Point(rect.Right, rect.Bottom));
            // A click without a drag snips nothing; the overlay stays for another try.
            if (bottomRight.X - topLeft.X < 2 || bottomRight.Y - topLeft.Y < 2)
            {
                Select(Rect.Empty);
                return;
            }
            _snip.Finish(_snip.Crop(new RectInt32(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y)), SnipMode.Rectangle);
        }

        // The path closed and cut out; Snipping Tool fills it in the accent colour, fading in 200 ms, as it's taken.
        private void SnipFreeform()
        {
            IReadOnlyList<Point> smoothed = FreeformPath.Smooth(_path);
            var clip = new RectInt32(_monitor.Bounds.X - _snip._screenBounds.X, _monitor.Bounds.Y - _snip._screenBounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height);
            RectInt32 bounds = FreeformPath.Bounds(smoothed, clip);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                ShowMode();
                return;
            }
            IconBitmap snip = FreeformPath.Cut(_snip._screen, bounds, FreeformPath.Mask(smoothed, bounds));

            double scale = Scale;
            foreach (Point point in smoothed)
                _flash.Points.Add(new Point((point.X - clip.X) / scale, (point.Y - clip.Y) / scale));
            var fade = new DoubleAnimation { From = 0.4, To = 0, Duration = TimeSpan.FromMilliseconds(200) };
            Storyboard.SetTarget(fade, _flash);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            var storyboard = new Storyboard { Children = { fade } };
            storyboard.Completed += (_, _) => _snip.Finish(snip, SnipMode.Freeform);
            storyboard.Begin();
        }

        private void AddToPath(Point position)
        {
            _stroke.Points.Add(position);
            double scale = Scale;
            _path.Add(new Point(
                _monitor.Bounds.X - _snip._screenBounds.X + position.X * scale,
                _monitor.Bounds.Y - _snip._screenBounds.Y + position.Y * scale));
        }

        private void Select(Rect rect)
        {
            ShowHole(rect);
            _outlines.Visibility = rect.IsEmpty || rect.Width == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (rect.IsEmpty)
                return;
            // On the centres of the pixels around the selection, each from corner to corner.
            (double left, double top, double right, double bottom) = (rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
            Place(_outline[0], left, top + 0.5, right, top + 0.5);
            Place(_outline[1], left + 0.5, top, left + 0.5, bottom);
            Place(_outline[2], right - 0.5, top, right - 0.5, bottom);
            Place(_outline[3], left, bottom - 0.5, right, bottom - 0.5);
        }

        private static void Place(Line line, double x1, double y1, double x2, double y2)
        {
            (line.X1, line.Y1, line.X2, line.Y2) = (x1, y1, x2, y2);
        }

        // The pointer kept on this monitor while a selection is dragged.
        private Point Clamp(Point position) =>
            new(Math.Clamp(position.X, 0, _surface.ActualWidth), Math.Clamp(position.Y, 0, _surface.ActualHeight));

        private PointInt32 ToScreen(Point position)
        {
            double scale = Scale;
            return new PointInt32(
                _monitor.Bounds.X + (int)Math.Round(position.X * scale), _monitor.Bounds.Y + (int)Math.Round(position.Y * scale));
        }
    }
}

/// <summary>Takes the keyboard (for Esc) and shows the mode's pointer, as Snipping Tool's overlay.</summary>
internal sealed partial class SnipSurface : UserControl
{
    public SnipSurface()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        // Hit-testable everywhere, for the pointer.
        Background = new SolidColorBrush(Colors.Transparent);
    }

    public void SetCursor(InputSystemCursorShape shape) => ProtectedCursor = InputSystemCursor.Create(shape);
}
