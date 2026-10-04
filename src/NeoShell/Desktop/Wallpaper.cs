using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Desktop;

/// <summary>
/// The wallpaper on every monitor, and the desktop icons on the primary one (shell mode only; Explorer draws both
/// otherwise). Follows changes to the wallpaper, the background colour and the displays.
/// </summary>
internal sealed class Wallpaper(SettingsStore settings) : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DesktopIcons _icons = new(settings);
    private readonly List<WallpaperWindow> _windows = [];
    private WallpaperSettings? _settings;
    private BitmapImage? _image;
    private bool _updateQueued;
    private bool _displaysChanged;
    private int _updateVersion;

    public void Show() => QueueUpdate(displaysChanged: true);

    public void Dispose()
    {
        _updateVersion++;
        CloseWindows();
        _icons.Dispose();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessages.DisplayChange:
                QueueUpdate(displaysChanged: true);
                break;
            case WindowMessages.SettingChange:
            case WindowMessages.SysColorChange:
                QueueUpdate(displaysChanged: false);
                // Folder options (hidden files, extensions), the theme or the work area may have changed.
                _icons.QueueRefresh();
                break;
        }
        return null;
    }

    // Every wallpaper window receives the same broadcast, and changes tend to come in bursts: update once afterwards.
    private void QueueUpdate(bool displaysChanged)
    {
        _displaysChanged |= displaysChanged;
        if (_updateQueued)
            return;

        _updateQueued = true;
        _dispatcher.Post(Update);
    }

    private async void Update()
    {
        _updateQueued = false;
        int version = ++_updateVersion;
        try
        {
            WallpaperSettings settings = WallpaperSettings.Read();
            bool settingsChanged = settings != _settings;
            if (settingsChanged)
            {
                BitmapImage? image = await LoadImageAsync(settings.ImagePath);
                if (version != _updateVersion)
                    return; // a newer update or Dispose took over while loading

                _settings = settings;
                _image = image;
                Log.Info($"Wallpaper: {settings.ImagePath ?? "none"}, {settings.Style}, background {settings.Background}");
            }

            if (_displaysChanged)
            {
                _displaysChanged = false;
                CreateWindows();
            }
            else if (!settingsChanged)
            {
                return;
            }

            foreach (WallpaperWindow window in _windows)
                window.SetWallpaper(_settings!, _image);
        }
        catch (Exception ex)
        {
            Log.Error("Updating the wallpaper failed", ex);
        }
    }

    private void CreateWindows()
    {
        CloseWindows();

        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        var virtualScreen = WallpaperLayout.Union(monitors.Select(monitor => monitor.Bounds));
        foreach (DisplayMonitor monitor in monitors)
        {
            Log.Info($"Monitor {WallpaperWindow.Format(monitor.Bounds)}, work area {WallpaperWindow.Format(monitor.WorkArea)}, "
                + $"{monitor.Dpi} DPI{(monitor.IsPrimary ? ", primary" : "")}");
            var window = new WallpaperWindow(monitor, virtualScreen, OnMessage, monitor.IsPrimary ? _icons : null);
            window.AppWindow.Show(activateWindow: false);
            _windows.Add(window);
        }
        Log.Info($"Wallpaper windows for {monitors.Count} monitor(s), virtual screen {WallpaperWindow.Format(virtualScreen)}");
    }

    private void CloseWindows()
    {
        foreach (WallpaperWindow window in _windows)
            window.Close();
        _windows.Clear();
    }

    private static async Task<BitmapImage?> LoadImageAsync(string? path)
    {
        if (path is null)
            return null;

        try
        {
            // Read it all first: Windows rewrites the wallpaper file in place, so don't hold it open.
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Log.Info($"Wallpaper file read: {bytes.Length} bytes");
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            Log.Info($"Wallpaper decoded: {image.PixelWidth}x{image.PixelHeight}");
            return image;
        }
        catch (Exception ex)
        {
            // A missing, locked or undecodable file just means a plain background colour.
            Log.Warn($"Could not load wallpaper {path}", ex);
            return null;
        }
    }
}
