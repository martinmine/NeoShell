using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using Windows.UI;

namespace NeoShell.Desktop;

/// <summary>
/// <c>IDesktopWallpaper</c> while NeoShell is the shell, answering as Explorer's desktop does and storing everything
/// where Explorer does; the wallpaper windows follow through the <c>WM_SETTINGCHANGE</c> each change sends.
/// </summary>
internal sealed class WallpaperService(Slideshow slideshow) : DesktopWallpaperServer
{
    private const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    private const string ColorsKey = @"Control Panel\Colors";
    private const string HistoryKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
    private const int HistoryLength = 5;

    protected override void SetWallpaper(string? monitorPath, string path)
    {
        if (path.Length > 0 && !File.Exists(path))
            throw new FileNotFoundException("No such picture", path);

        // A picture ends the slideshow.
        if (slideshow.IsRunning)
        {
            SlideshowSettings.ClearSources();
            slideshow.Refresh();
        }

        if (monitorPath is null)
        {
            WallpaperRegistry.ClearMonitorImages();
            DesktopBackground.SetWallpaper(path);
        }
        else
        {
            // Every monitor keeps its picture as its own value from now on; this one gets the new one.
            WallpaperSettings current = WallpaperSettings.Read();
            IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
            var images = new List<(string MonitorPath, string Path)>();
            int changed = 0;
            foreach (DisplayMonitor monitor in monitors)
            {
                bool target = string.Equals(monitor.DevicePath, monitorPath, StringComparison.OrdinalIgnoreCase);
                if (target)
                    changed = images.Count;
                if ((target ? path : current.ImageFor(monitor.DevicePath)) is { Length: > 0 } image)
                    images.Add((monitor.DevicePath, image));
            }
            // A COM call can't wait asynchronously; the pictures are read and copied on the thread pool meanwhile.
            Task.Run(() => WallpaperRegistry.WriteMonitorImagesAsync(images, Math.Min(changed, images.Count - 1))).GetAwaiter().GetResult();
            // As Explorer: Wallpaper names the transcoded copy; setting it tells everyone (us too) to re-read.
            DesktopBackground.SetWallpaper(images.Count > 0 ? WallpaperRegistry.TranscodedWallpaperPath : "");
        }

        if (path.Length > 0)
            AddToHistory(path);
    }

    protected override string GetWallpaper(string? monitorPath)
    {
        WallpaperSettings settings = WallpaperSettings.Read();
        if (monitorPath is not null)
            return (DisplayMonitor.GetAll().Any(monitor => string.Equals(monitor.DevicePath, monitorPath, StringComparison.OrdinalIgnoreCase))
                ? settings.ImageFor(monitorPath)
                : settings.ImagePath) ?? "";

        // One picture for all only when every monitor shows it, and never during a slideshow.
        if (slideshow.IsRunning)
            return "";
        List<string?> images = DisplayMonitor.GetAll().Select(monitor => settings.ImageFor(monitor.DevicePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return images.Count == 1 ? images[0] ?? "" : "";
    }

    protected override uint BackgroundColor
    {
        get
        {
            using RegistryKey? colors = Registry.CurrentUser.OpenSubKey(ColorsKey);
            Color color = WallpaperSettings.ParseColor(colors?.GetValue("Background")?.ToString());
            return (uint)(color.R | color.G << 8 | color.B << 16);
        }
        set
        {
            DesktopBackground.SetColor(value);
            using RegistryKey colors = Registry.CurrentUser.CreateSubKey(ColorsKey);
            colors.SetValue("Background", $"{value & 0xFF} {value >> 8 & 0xFF} {value >> 16 & 0xFF}");
        }
    }

    protected override DesktopWallpaperPosition Position
    {
        get => WallpaperSettings.Read().Style switch
        {
            WallpaperStyle.Center => DesktopWallpaperPosition.Center,
            WallpaperStyle.Tile => DesktopWallpaperPosition.Tile,
            WallpaperStyle.Stretch => DesktopWallpaperPosition.Stretch,
            WallpaperStyle.Fit => DesktopWallpaperPosition.Fit,
            WallpaperStyle.Span => DesktopWallpaperPosition.Span,
            _ => DesktopWallpaperPosition.Fill,
        };
        set
        {
            (string style, string tile) = WallpaperSettings.FormatStyle(value switch
            {
                DesktopWallpaperPosition.Center => WallpaperStyle.Center,
                DesktopWallpaperPosition.Tile => WallpaperStyle.Tile,
                DesktopWallpaperPosition.Stretch => WallpaperStyle.Stretch,
                DesktopWallpaperPosition.Fit => WallpaperStyle.Fit,
                DesktopWallpaperPosition.Span => WallpaperStyle.Span,
                _ => WallpaperStyle.Fill,
            });
            using (RegistryKey desktop = Registry.CurrentUser.CreateSubKey(WallpaperRegistry.DesktopKey))
            {
                desktop.SetValue("WallpaperStyle", style);
                desktop.SetValue("TileWallpaper", tile);
            }
            // Setting the same picture again makes Windows apply the fit and tell everyone.
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WallpaperRegistry.DesktopKey);
            DesktopBackground.SetWallpaper(key?.GetValue("Wallpaper")?.ToString() ?? "");
        }
    }

    protected override void SetSlideshow(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        SlideshowSettings.WriteSources(paths);
        slideshow.Refresh();
    }

    protected override IReadOnlyList<string> GetSlideshow() => SlideshowSettings.Read().Sources;

    protected override (bool Shuffle, uint Interval) SlideshowOptions
    {
        get
        {
            SlideshowSettings settings = SlideshowSettings.Read();
            return (settings.Shuffle, settings.Interval);
        }
        set
        {
            SlideshowSettings.WriteOptions(value.Shuffle, value.Interval);
            slideshow.Refresh();
        }
    }

    protected override void AdvanceSlideshow()
    {
        if (!slideshow.IsRunning)
            throw new COMException("No slideshow", E_UNEXPECTED);
        slideshow.Advance();
    }

    protected override bool Enabled
    {
        get
        {
            WallpaperSettings settings = WallpaperSettings.Read();
            return settings.ImagePath is not null || settings.MonitorImages.Count > 0;
        }
        set
        {
            if (value == Enabled)
                return;
            WallpaperRegistry.ClearMonitorImages();
            // Off clears the picture; on brings back the last one Explorer transcoded, which must still exist.
            if (value && !File.Exists(WallpaperRegistry.TranscodedWallpaperPath))
                throw new FileNotFoundException("No previous picture", WallpaperRegistry.TranscodedWallpaperPath);
            DesktopBackground.SetWallpaper(value ? WallpaperRegistry.TranscodedWallpaperPath : "");
        }
    }

    protected override bool SlideshowRunning => slideshow.IsRunning;

    /// <summary>Settings' "Recent images", newest first, as Explorer keeps them.</summary>
    private static void AddToHistory(string path)
    {
        using RegistryKey history = Registry.CurrentUser.CreateSubKey(HistoryKey);
        List<string> paths = Enumerable.Range(0, HistoryLength)
            .Select(i => history.GetValue($"BackgroundHistoryPath{i}")?.ToString())
            .OfType<string>()
            .Where(old => !string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
            .Prepend(path)
            .Take(HistoryLength)
            .ToList();
        for (int i = 0; i < paths.Count; i++)
            history.SetValue($"BackgroundHistoryPath{i}", paths[i]);
    }
}
