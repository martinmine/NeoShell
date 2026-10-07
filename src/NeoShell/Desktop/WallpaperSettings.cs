using Microsoft.Win32;
using Windows.UI;

namespace NeoShell.Desktop;

/// <summary>The "Choose a fit" options in Settings → Personalization → Background.</summary>
public enum WallpaperStyle { Fill, Fit, Stretch, Center, Tile, Span }

/// <summary>The user's wallpaper, as Explorer reads it from the registry.</summary>
/// <param name="ImagePath">Full path of the image, or null for a solid colour.</param>
/// <param name="ImageWriteTime">Detects the image being replaced in place (Windows reuses one file for it).</param>
/// <param name="MonitorImages">
/// The images of monitors that have their own (by <see cref="Interop.Windowing.DisplayMonitor.DevicePath"/>); the
/// others show <paramref name="ImagePath"/>.
/// </param>
public sealed record WallpaperSettings(
    string? ImagePath, DateTime ImageWriteTime, WallpaperStyle Style, Color Background, IReadOnlyDictionary<string, string> MonitorImages)
{
    public static WallpaperSettings Read()
    {
        using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(WallpaperRegistry.DesktopKey);
        using RegistryKey? colors = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");

        string? path = WallpaperRegistry.ReadImagePath(desktop);
        return new WallpaperSettings(
            path,
            path is null ? default : File.GetLastWriteTimeUtc(path),
            ParseStyle(desktop?.GetValue("WallpaperStyle")?.ToString(), desktop?.GetValue("TileWallpaper")?.ToString()),
            ParseColor(colors?.GetValue("Background")?.ToString()),
            WallpaperRegistry.ReadMonitorImages(desktop));
    }

    /// <summary>The image on the monitor with this device path, or null for none.</summary>
    public string? ImageFor(string monitorPath) => MonitorImages.TryGetValue(monitorPath, out string? path) ? path : ImagePath;

    /// <summary>Maps the <c>WallpaperStyle</c> and <c>TileWallpaper</c> registry values; unknown values mean Fill.</summary>
    public static WallpaperStyle ParseStyle(string? wallpaperStyle, string? tileWallpaper) => wallpaperStyle?.Trim() switch
    {
        "0" => tileWallpaper?.Trim() == "1" ? WallpaperStyle.Tile : WallpaperStyle.Center,
        "2" => WallpaperStyle.Stretch,
        "6" => WallpaperStyle.Fit,
        "22" => WallpaperStyle.Span,
        _ => WallpaperStyle.Fill,
    };

    /// <summary>The <c>WallpaperStyle</c> and <c>TileWallpaper</c> values Explorer writes for a style.</summary>
    public static (string WallpaperStyle, string TileWallpaper) FormatStyle(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Center => ("0", "0"),
        WallpaperStyle.Tile => ("0", "1"),
        WallpaperStyle.Stretch => ("2", "0"),
        WallpaperStyle.Fit => ("6", "0"),
        WallpaperStyle.Span => ("22", "0"),
        _ => ("10", "0"),
    };

    /// <summary>Parses an <c>"R G B"</c> colour value; anything else gives black.</summary>
    public static Color ParseColor(string? value)
    {
        string[] parts = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && byte.TryParse(parts[0], out byte r)
            && byte.TryParse(parts[1], out byte g)
            && byte.TryParse(parts[2], out byte b))
        {
            return new Color { A = 255, R = r, G = g, B = b };
        }
        return new Color { A = 255 };
    }

    // A record compares the dictionary by reference; a re-read must equal the settings it repeats.
    public bool Equals(WallpaperSettings? other) =>
        other is not null
        && ImagePath == other.ImagePath
        && ImageWriteTime == other.ImageWriteTime
        && Style == other.Style
        && Background == other.Background
        && MonitorImages.Count == other.MonitorImages.Count
        && MonitorImages.All(pair => other.MonitorImages.TryGetValue(pair.Key, out string? path) && path == pair.Value);

    public override int GetHashCode() => HashCode.Combine(ImagePath, ImageWriteTime, Style, Background, MonitorImages.Count);
}
