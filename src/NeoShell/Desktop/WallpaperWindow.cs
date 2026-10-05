using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private readonly Canvas _canvas = new();
    private readonly FramelessWindow _frameless;
    private readonly WindowSubclass _messages;
    private readonly PinnedWindow _placement;
    private WallpaperSettings? _settings;
    private BitmapImage? _image;

    /// <param name="onMessage">Sees this window's messages; top-level windows receive the system broadcasts.</param>
    /// <param name="icons">The desktop icons to show, on the primary monitor only.</param>
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

    public void SetWallpaper(WallpaperSettings settings, BitmapImage? image)
    {
        _settings = settings;
        _image = image;
        _root.Background = new SolidColorBrush(settings.Background);
        Arrange();
    }

    /// <summary>Formats a rectangle for the log as <c>WxH at (X,Y)</c>.</summary>
    public static string Format(RectInt32 rect) => $"{rect.Width}x{rect.Height} at ({rect.X},{rect.Y})";

    private void Arrange()
    {
        _canvas.Children.Clear();
        if (_settings is null)
            return; // not given a wallpaper yet

        string monitor = Format(_monitor.Bounds);
        if (_image is null || _root.XamlRoot is null)
        {
            Log.Info($"Wallpaper on {monitor}: not drawn, {(_image is null ? "no image" : "no XamlRoot yet")}");
            return;
        }

        // Layout is in physical pixels; XAML positions are in effective pixels.
        double scale = _root.XamlRoot.RasterizationScale;
        var imageSize = new SizeInt32(_image.PixelWidth, _image.PixelHeight);
        IReadOnlyList<RectInt32> rects = WallpaperLayout.Arrange(_settings.Style, imageSize, _monitor.Bounds, _virtualScreen);
        Log.Info($"Wallpaper on {monitor}: image {imageSize.Width}x{imageSize.Height}, {_settings.Style}, scale {scale}, "
            + $"root {_root.ActualWidth}x{_root.ActualHeight}, window {AppWindow.Size.Width}x{AppWindow.Size.Height} "
            + $"at ({AppWindow.Position.X},{AppWindow.Position.Y}), visible {AppWindow.IsVisible}, "
            + $"{rects.Count} rect(s){(rects.Count > 0 ? $", first {Format(rects[0])}" : "")}");

        foreach (RectInt32 rect in rects)
        {
            var element = new Image
            {
                Source = _image,
                Stretch = Stretch.Fill,
                Width = rect.Width / scale,
                Height = rect.Height / scale,
            };
            element.ImageFailed += (_, e) => Log.Warn($"Wallpaper on {monitor}: image failed: {e.ErrorMessage}");
            Canvas.SetLeft(element, rect.X / scale);
            Canvas.SetTop(element, rect.Y / scale);
            _canvas.Children.Add(element);
        }
    }
}
