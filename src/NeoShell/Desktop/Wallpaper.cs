using Microsoft.UI.Dispatching;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Desktop;

/// <summary>
/// The wallpaper and the desktop icons on every monitor (shell mode only; Explorer draws both
/// otherwise). Follows changes to the wallpaper, the background colour and the displays, runs the slideshow and
/// serves <c>IDesktopWallpaper</c>, as Explorer's desktop does.
/// </summary>
/// <param name="closeRequested">Alt+F4 on the desktop.</param>
internal sealed class Wallpaper(SettingsStore settings, Action closeRequested) : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DesktopIcons _icons = new(settings);
    private readonly List<WallpaperWindow> _windows = [];
    private readonly WindowEvents _shown = new(WindowEvent.Shown, WindowEvent.Shown);
    private Slideshow? _slideshow;
    private WallpaperService? _service;
    private WallpaperSettings? _settings;
    private Dictionary<string, WallpaperImage?> _images = [];
    private bool _updateQueued;
    private bool _displaysChanged;
    private int _fadeMilliseconds;
    private int _updateVersion;

    public void Show()
    {
        QueueUpdate(displaysChanged: true);
        _shown.Raised += (_, hwnd) => RaiseAboveWallpaper(hwnd);

        _slideshow = new Slideshow(OnSlideshowChanged);
        _slideshow.Refresh();
        _service = new WallpaperService(_slideshow);
        try
        {
            _service.Register();
        }
        catch (Exception ex)
        {
            Log.Error("Could not serve IDesktopWallpaper", ex);
        }
    }

    /// <summary>The window showing the wallpaper on the monitor (<see cref="DisplayMonitor.Handle"/>), if any.</summary>
    public WallpaperWindow? WindowOn(nint monitor) => _windows.FirstOrDefault(window => window.Monitor.Handle == monitor);

    public void Dispose()
    {
        _updateVersion++;
        _shown.Dispose();
        _service?.Dispose();
        _slideshow?.Dispose();
        CloseWindows();
        _icons.Dispose();
    }

    /// <summary>
    /// Puts a window an app has just shown on top of the other windows when it went below the wallpaper. Windows puts the
    /// window of an app that may not take the foreground below the lowest window of the thread in front
    /// (win32k's <c>CalcForegroundInsertAfter</c>); with NeoShell in front (its taskbar clicked) that's the wallpaper, a
    /// window of the same thread, so the window opened out of sight. Explorer's desktop has a thread of its own, and with
    /// its taskbar in front such a window opens on top of the others, behind the taskbar.
    /// </summary>
    private void RaiseAboveWallpaper(nint hwnd)
    {
        if (TopLevelWindows.IsMinimized(hwnd) || TopLevelWindows.IsDesktop(hwnd))
            return;
        if (_windows.Any(window => TopLevelWindows.IsBelow(hwnd, window.Handle)))
            TopLevelWindows.BringAboveOthers(hwnd);
    }

    // The slideshow's change comes with a broadcast of its own; the update it queues crossfades.
    private void OnSlideshowChanged(int fadeMilliseconds)
    {
        _fadeMilliseconds = fadeMilliseconds;
        QueueUpdate(displaysChanged: false);
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
        int fade = _fadeMilliseconds;
        _fadeMilliseconds = 0;
        try
        {
            WallpaperSettings settings = WallpaperSettings.Read();
            bool settingsChanged = settings != _settings;
            if (settingsChanged)
            {
                var images = new Dictionary<string, WallpaperImage?>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in settings.MonitorImages.Values.Append(settings.ImagePath).OfType<string>())
                {
                    if (!images.ContainsKey(path))
                        images[path] = await LoadImageAsync(path);
                }
                if (version != _updateVersion)
                    return; // a newer update or Dispose took over while loading

                _settings = settings;
                _images = images;
                Log.Info($"Wallpaper: {settings.ImagePath ?? "none"}, {settings.Style}, background {settings.Background}"
                    + string.Concat(settings.MonitorImages.Select(pair => $", {pair.Key}: {pair.Value}")));
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
            {
                string? path = _settings!.ImageFor(window.Monitor.DevicePath);
                window.SetWallpaper(_settings, path is null ? null : _images.GetValueOrDefault(path), settingsChanged ? fade : 0);
            }
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
            var window = new WallpaperWindow(monitor, virtualScreen, OnMessage, _icons, closeRequested);
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

    private static async Task<WallpaperImage?> LoadImageAsync(string? path)
    {
        if (path is null)
            return null;

        try
        {
            // Read it all first: Windows rewrites the wallpaper file in place, so don't hold it open.
            byte[] bytes = await File.ReadAllBytesAsync(path);
            SizeInt32 size = await Pictures.GetSizeAsync(bytes);
            Log.Info($"Wallpaper file read: {bytes.Length} bytes, {size.Width}x{size.Height}");
            return new WallpaperImage(bytes, size);
        }
        catch (Exception ex)
        {
            // A missing, locked or undecodable file just means a plain background colour.
            Log.Warn($"Could not load wallpaper {path}", ex);
            return null;
        }
    }
}

/// <summary>A picture's file contents and its size in pixels; each monitor decodes it at the size it draws it.</summary>
internal sealed record WallpaperImage(byte[] Bytes, SizeInt32 Size);
