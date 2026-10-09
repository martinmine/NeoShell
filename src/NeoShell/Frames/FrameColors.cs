using System.Globalization;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Frames;

/// <summary>Turns a <see cref="FrameStyle"/> into the DWM attributes it stands for.</summary>
public static class FrameColors
{
    /// <summary>Windows' accent colour, read as the style is applied.</summary>
    public const string Accent = "Accent";
    /// <summary>No border at all (<c>DWMWA_COLOR_NONE</c>).</summary>
    public const string None = "None";
    /// <summary>White or black text, whichever reads on the caption colour.</summary>
    public const string Contrast = "Contrast";

    /// <param name="accent">The accent colour, 0xAARRGGBB.</param>
    public static FrameAttributes ToAttributes(FrameStyle style, uint accent)
    {
        uint? caption = Resolve(style.CaptionColor, accent);
        return new FrameAttributes
        {
            Backdrop = style.Backdrop switch
            {
                FrameBackdrop.None => SystemBackdrop.None,
                FrameBackdrop.Mica => SystemBackdrop.Mica,
                FrameBackdrop.MicaAlt => SystemBackdrop.MicaAlt,
                FrameBackdrop.Acrylic => SystemBackdrop.Acrylic,
                _ => null,
            },
            DarkMode = style.Theme switch
            {
                FrameTheme.Light => false,
                FrameTheme.Dark => true,
                _ => null,
            },
            CaptionColor = caption,
            TextColor = style.TextColor == Contrast ? caption is { } on ? ContrastOn(on) : null : Resolve(style.TextColor, accent),
            BorderColor = style.BorderColor == None ? 0 : Resolve(style.BorderColor, accent),
            Corners = style.Corners switch
            {
                FrameCorners.Square => CornerPreference.Square,
                FrameCorners.Round => CornerPreference.Round,
                FrameCorners.SmallRound => CornerPreference.SmallRound,
                _ => null,
            },
            BasicFrame = style.BasicFrame,
        };
    }

    /// <summary>
    /// The parts a window had set that its new attributes leave unset: they go back to Windows' own before the new
    /// ones are applied. A part set either way is simply overwritten.
    /// </summary>
    public static FrameParts PartsToReset(FrameAttributes? before, FrameAttributes after) =>
        (before?.Parts ?? FrameParts.None) & ~after.Parts;

    /// <summary><c>#RRGGBB</c> or <see cref="Accent"/> as 0xFFRRGGBB; null for anything else (unset).</summary>
    public static uint? Resolve(string? color, uint accent) =>
        color == Accent ? accent | 0xFF000000 : Parse(color);

    /// <summary><c>#RRGGBB</c> as 0xFFRRGGBB, or null.</summary>
    public static uint? Parse(string? color) =>
        color is ['#', .. { Length: 6 } hex] && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? 0xFF000000 | rgb
            : null;

    public static string Format(uint argb) => $"#{argb & 0xFFFFFF:X6}";

    /// <summary>Black on light colours and white on dark ones, as Windows 10 chose the title text on an accent colour.</summary>
    public static uint ContrastOn(uint argb)
    {
        double luminance = 0.2126 * ((argb >> 16) & 0xFF) + 0.7152 * ((argb >> 8) & 0xFF) + 0.0722 * (argb & 0xFF);
        return luminance > 140 ? 0xFF000000 : 0xFFFFFFFF;
    }
}
