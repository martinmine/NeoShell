using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace NeoShell.Capture;

/// <summary>
/// Win+Shift+S as the shell. Snipping Tool can't show its overlay without Explorer, so NeoShell snips by itself: the
/// screen freezes, dimmed, and the rectangle dragged out shows at full brightness; letting go keeps that part as
/// Snipping Tool does (<see cref="Screenshots.Keep"/>). Esc or a right-click cancels.
/// </summary>
internal sealed class ScreenSnip
{
    private static ScreenSnip? s_current;

    private readonly List<SnipWindow> _windows = [];

    public static void Start()
    {
        if (s_current is not null)
            return;

        // The pictures first, so the overlay isn't in them.
        List<(DisplayMonitor Monitor, IconBitmap Picture)> screens;
        try
        {
            screens = [.. DisplayMonitor.GetAll().Select(monitor => (monitor, ScreenCapture.Capture(monitor.Bounds)))];
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warn("Could not take a screenshot", ex);
            return;
        }
        s_current = new ScreenSnip(screens);
    }

    private ScreenSnip(List<(DisplayMonitor Monitor, IconBitmap Picture)> screens)
    {
        foreach ((DisplayMonitor monitor, IconBitmap picture) in screens)
        {
            var window = new SnipWindow(monitor, picture, this);
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
    }

    /// <summary>Ends the snip, keeping <paramref name="picture"/> unless it's null (cancelled).</summary>
    private void End(IconBitmap? picture, nint owner)
    {
        if (picture is not null)
            Screenshots.Keep(picture, owner);
        else
            Log.Info("Snip cancelled");
        foreach (SnipWindow window in _windows)
            window.Close();
        _windows.Clear();
        s_current = null;
    }

    /// <summary>One monitor's frozen picture with the selection, in the topmost band over everything.</summary>
    private sealed class SnipWindow : Window
    {
        private readonly IconBitmap _picture;
        private readonly ScreenSnip _snip;
        private readonly FramelessWindow _frameless;
        private readonly PinnedWindow _placement;
        private readonly SnipSurface _surface = new();
        private readonly RectangleGeometry _all = new();
        private readonly RectangleGeometry _selected = new();
        private readonly Rectangle _outline = new()
        {
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 1,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };
        private Point? _start;

        public SnipWindow(DisplayMonitor monitor, IconBitmap picture, ScreenSnip snip)
        {
            _picture = picture;
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

            var dim = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { _all, _selected } };
            var root = new Grid
            {
                Children =
                {
                    new Image { Source = AppIcons.ToImageSource(picture), Stretch = Stretch.Fill },
                    new Path { Data = dim, Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), IsHitTestVisible = false },
                    _outline,
                },
            };
            root.SizeChanged += (_, e) => _all.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
            _surface.Content = root;
            _surface.PointerPressed += Surface_PointerPressed;
            _surface.PointerMoved += Surface_PointerMoved;
            _surface.PointerReleased += Surface_PointerReleased;
            _surface.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Escape)
                    _snip.End(null, 0);
            };
            Content = _surface;
            Activated += (_, e) =>
            {
                if (e.WindowActivationState != WindowActivationState.Deactivated)
                    _surface.Focus(FocusState.Programmatic);
            };
            Closed += (_, _) =>
            {
                _placement.Dispose();
                _frameless.Dispose();
            };
        }

        public nint Handle { get; }

        private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(_surface);
            if (point.Properties.IsRightButtonPressed)
            {
                _snip.End(null, 0);
                return;
            }
            _start = point.Position;
            _surface.CapturePointer(e.Pointer);
            Select(new Rect(point.Position, point.Position));
        }

        private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_start is { } start)
                Select(new Rect(start, e.GetCurrentPoint(_surface).Position));
        }

        private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_start is not { } start)
                return;
            _start = null;
            _surface.ReleasePointerCapture(e.Pointer);

            // Effective pixels to the picture's own, kept inside it.
            double ratio = _picture.Width / _surface.ActualWidth;
            Rect rect = new(start, e.GetCurrentPoint(_surface).Position);
            int left = Math.Clamp((int)Math.Round(rect.X * ratio), 0, _picture.Width);
            int top = Math.Clamp((int)Math.Round(rect.Y * ratio), 0, _picture.Height);
            int right = Math.Clamp((int)Math.Round(rect.Right * ratio), 0, _picture.Width);
            int bottom = Math.Clamp((int)Math.Round(rect.Bottom * ratio), 0, _picture.Height);
            // A click without a drag snips nothing; the overlay stays for another try.
            if (right - left < 2 || bottom - top < 2)
            {
                Select(Rect.Empty);
                return;
            }
            _snip.End(Crop(_picture, new RectInt32(left, top, right - left, bottom - top)), Handle);
        }

        private void Select(Rect rect)
        {
            _selected.Rect = rect.IsEmpty ? default : rect;
            _outline.Visibility = rect.IsEmpty || rect.Width == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (rect.IsEmpty)
                return;
            _outline.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            _outline.Width = rect.Width;
            _outline.Height = rect.Height;
        }

        private static IconBitmap Crop(IconBitmap picture, RectInt32 area)
        {
            var pixels = new byte[area.Width * area.Height * 4];
            for (int row = 0; row < area.Height; row++)
                Array.Copy(picture.Pixels, ((area.Y + row) * picture.Width + area.X) * 4, pixels, row * area.Width * 4, area.Width * 4);
            return new IconBitmap(area.Width, area.Height, pixels);
        }
    }
}

/// <summary>Takes the keyboard (for Esc) and shows the crosshair, as Snipping Tool's overlay.</summary>
internal sealed partial class SnipSurface : UserControl
{
    public SnipSurface()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
    }
}
