using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell.Desktop;

/// <summary>Shows the wallpaper on one monitor, behind every other window, and the desktop icons over it.</summary>
internal sealed class WallpaperWindow : Window
{
    private readonly DisplayMonitor _monitor;
    private readonly RectInt32 _virtualScreen;
    private readonly Grid _root = new();
    private Canvas _canvas = new();
    private readonly FramelessWindow _frameless;
    private readonly WindowSubclass _messages;
    private readonly PinnedWindow _placement;
    private WallpaperSettings? _settings;
    private WallpaperImage? _image;
    private int _arrangeVersion;

    /// <param name="onMessage">Sees this window's messages; top-level windows receive the system broadcasts.</param>
    /// <param name="icons">The desktop icons, of which this window shows those on its monitor.</param>
    /// <param name="closeRequested">Alt+F4 on the desktop, which doesn't close it: Explorer asks to shut down instead.</param>
    public WallpaperWindow(DisplayMonitor monitor, RectInt32 virtualScreen, MessageHandler onMessage, DesktopIcons? icons, Action closeRequested)
    {
        _monitor = monitor;
        _virtualScreen = virtualScreen;

        _root.Children.Add(_canvas);
        _root.SizeChanged += (_, _) => Arrange();
        _root.Loaded += (_, _) => _root.XamlRoot.Changed += (_, _) => Arrange();
        Content = _root;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            closeRequested();
        };

        // Not AppWindow.IsShownInSwitchers: it goes through the taskbar and throws when there is none.
        nint hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow);
        _frameless = new FramelessWindow(hwnd);
        Peek.Exclude(hwnd);
        _messages = new WindowSubclass(hwnd, onMessage);
        _placement = new PinnedWindow(hwnd, monitor.Bounds, PinnedLayer.Bottom);

        if (icons is not null)
            _root.Children.Add(new DesktopIconsView(icons, hwnd, monitor));

        Closed += (_, _) =>
        {
            _placement.Dispose();
            _messages.Dispose();
            _frameless.Dispose();
        };
    }

    /// <param name="fadeMilliseconds">
    /// A slideshow's crossfade, linear as Explorer's; 0 replaces the picture at once, as Explorer does for any other
    /// change.
    /// </param>
    public void SetWallpaper(WallpaperSettings settings, WallpaperImage? image, int fadeMilliseconds = 0)
    {
        _settings = settings;
        _image = image;
        _root.Background = new SolidColorBrush(settings.Background);
        Arrange(fadeMilliseconds);
    }

    public DisplayMonitor Monitor => _monitor;

    /// <summary>Formats a rectangle for the log as <c>WxH at (X,Y)</c>.</summary>
    public static string Format(RectInt32 rect) => $"{rect.Width}x{rect.Height} at ({rect.X},{rect.Y})";

    // The picture is decoded before it's shown (off the UI thread, at the size it's drawn, as Explorer transcodes it per
    // monitor): a change never shows a half-drawn picture, and a crossfade starts with the new picture ready (WinUI
    // decodes a BitmapImage only once it's drawn, and gives no sign of when that's done).
    private async void Arrange(int fadeMilliseconds = 0)
    {
        int version = ++_arrangeVersion;
        if (_settings is not { } settings)
            return; // not given a wallpaper yet

        string monitor = Format(_monitor.Bounds);
        if (_root.XamlRoot is null)
        {
            Log.Info($"Wallpaper on {monitor}: not drawn, no XamlRoot yet");
            return;
        }

        // Layout is in physical pixels; XAML positions are in effective pixels.
        double scale = _root.XamlRoot.RasterizationScale;
        var canvas = new Canvas { Background = new SolidColorBrush(settings.Background) };
        if (_image is { } image)
        {
            IReadOnlyList<RectInt32> rects = WallpaperLayout.Arrange(settings.Style, image.Size, _monitor.Bounds, _virtualScreen);
            Log.Info($"Wallpaper on {monitor}: image {image.Size.Width}x{image.Size.Height}, {settings.Style}, scale {scale}, "
                + $"root {_root.ActualWidth}x{_root.ActualHeight}, window {AppWindow.Size.Width}x{AppWindow.Size.Height} "
                + $"at ({AppWindow.Position.X},{AppWindow.Position.Y}), visible {AppWindow.IsVisible}, "
                + $"{rects.Count} rect(s){(rects.Count > 0 ? $", first {Format(rects[0])}" : "")}");

            ImageSource? source = null;
            if (rects.Count > 0)
            {
                try
                {
                    var size = new SizeInt32(rects[0].Width, rects[0].Height);
                    source = AppIcons.ToImageSource(await Task.Run(() => Pictures.DecodeAsync(image.Bytes, size)));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Wallpaper on {monitor}: could not decode the picture", ex);
                }
                if (version != _arrangeVersion)
                    return; // a newer layout took over while decoding
            }

            foreach (RectInt32 rect in source is null ? [] : rects)
            {
                var element = new Image { Source = source, Stretch = Stretch.Fill, Width = rect.Width / scale, Height = rect.Height / scale };
                Canvas.SetLeft(element, rect.X / scale);
                Canvas.SetTop(element, rect.Y / scale);
                canvas.Children.Add(element);
            }
        }

        Canvas old = _canvas;
        _canvas = canvas;
        _root.Children.Insert(_root.Children.IndexOf(old), canvas);
        if (fadeMilliseconds <= 0 || old.Children.Count == 0)
        {
            _root.Children.Remove(old);
            return;
        }

        // The new picture is under the old one, which fades out: the same as the new one fading in. XAML fades each
        // element on its own rather than the canvas as a whole, so the old background would show through the old
        // picture: it goes (the new one, below, has the same colour unless that changed too).
        old.Background = null;
        var fade = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(fadeMilliseconds) };
        Storyboard.SetTarget(fade, old);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Storyboard { Children = { fade } };
        story.Completed += (_, _) => _root.Children.Remove(old);
        story.Begin();
    }
}
