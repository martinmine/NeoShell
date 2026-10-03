using Microsoft.Win32;
using Windows.UI;

namespace NeoShell.Desktop;

/// <summary>The "Choose a fit" options in Settings → Personalization → Background.</summary>
public enum WallpaperStyle { Fill, Fit, Stretch, Center, Tile, Span }

/// <summary>The user's wallpaper, as Explorer reads it from the registry.</summary>
/// <param name="ImagePath">Full path of the image, or null for a solid colour.</param>
/// <param name="ImageWriteTime">Detects the image being replaced in place (Windows reuses one file for it).</param>
public sealed record WallpaperSettings(string? ImagePath, DateTime ImageWriteTime, WallpaperStyle Style, Color Background)
{
    public static WallpaperSettings Read()
    {
        using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        using RegistryKey? colors = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");

        string? path = desktop?.GetValue("Wallpaper")?.ToString();
        path = string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path);

        return new WallpaperSettings(
            path,
            path is null ? default : File.GetLastWriteTimeUtc(path),
            ParseStyle(desktop?.GetValue("WallpaperStyle")?.ToString(), desktop?.GetValue("TileWallpaper")?.ToString()),
            ParseColor(colors?.GetValue("Background")?.ToString()));
    }

    /// <summary>Maps the <c>WallpaperStyle</c> and <c>TileWallpaper</c> registry values; unknown values mean Fill.</summary>
    public static WallpaperStyle ParseStyle(string? wallpaperStyle, string? tileWallpaper) => wallpaperStyle?.Trim() switch
    {
        "0" => tileWallpaper?.Trim() == "1" ? WallpaperStyle.Tile : WallpaperStyle.Center,
        "2" => WallpaperStyle.Stretch,
        "6" => WallpaperStyle.Fit,
        "22" => WallpaperStyle.Span,
        _ => WallpaperStyle.Fill,
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
}
