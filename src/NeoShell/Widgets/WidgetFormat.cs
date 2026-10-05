using System.Globalization;
using Windows.UI;

namespace NeoShell.Widgets;

/// <summary>How the widgets write sizes, rates and colours.</summary>
public static class WidgetFormat
{
    /// <summary>The colours a graph can take: Windows' accent colours.</summary>
    public static readonly IReadOnlyList<string> Palette =
        ["#0078D4", "#8764B8", "#E3008C", "#D13438", "#FF8C00", "#FFB900", "#107C10", "#00B7C3"];

    /// <summary>Gigabytes with one decimal, as Task Manager writes memory: "5.2 GB".</summary>
    public static string Gigabytes(ulong bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " GB";

    /// <summary>A size as Explorer writes a drive's free space: "512 GB", "38.2 GB", "1.8 TB" (1024-based).</summary>
    public static string Size(ulong bytes)
    {
        double gigabytes = bytes / (1024.0 * 1024 * 1024);
        return gigabytes >= 1024 ? (gigabytes / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " TB"
            : gigabytes >= 100 ? gigabytes.ToString("0", CultureInfo.CurrentCulture) + " GB"
            : gigabytes.ToString("0.0", CultureInfo.CurrentCulture) + " GB";
    }

    /// <summary>Bits a second, as Task Manager writes network speed: "0 Kbps", "840 Kbps", "12.4 Mbps".</summary>
    public static string BitRate(double bytesPerSecond)
    {
        double bits = bytesPerSecond * 8;
        return bits >= 1_000_000_000 ? (bits / 1_000_000_000).ToString("0.0", CultureInfo.CurrentCulture) + " Gbps"
            : bits >= 1_000_000 ? (bits / 1_000_000).ToString("0.0", CultureInfo.CurrentCulture) + " Mbps"
            : (bits / 1000).ToString("0", CultureInfo.CurrentCulture) + " Kbps";
    }

    /// <summary>A colour written "#RRGGBB"; <paramref name="fallback"/> when it isn't one.</summary>
    public static Color ParseColor(string? text, string fallback)
    {
        if (TryParse(text, out Color color) || TryParse(fallback, out color))
            return color;
        return Color.FromArgb(255, 0, 0, 0);
    }

    private static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }
        color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
