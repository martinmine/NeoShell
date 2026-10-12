using static NeoShell.Themes.Windows98.Win98Art;
using static NeoShell.Themes.Windows98.Win98Painting;

namespace NeoShell.Themes.Windows98;

/// <summary>One of Start's items: its text with an ampersand before the underlined letter, its place, whether it opens a submenu.</summary>
public sealed record Win98StartItem(string Key, string Text, int Top, bool Submenu);

/// <summary>Start laid out: its size, the items and the separators' tops.</summary>
public sealed record Win98StartLayout(int Width, int Height, IReadOnlyList<Win98StartItem> Items, IReadOnlyList<int> Separators);

/// <summary>
/// Lays out and draws 98's Start menu as measured (docs/design/theme-98.md): a raised frame, the "Windows98" banner down
/// the left, 32-pixel items with 32-pixel icons, and etched separators.
/// </summary>
public static class Win98StartMenuPainter
{
    /// <summary>The frame (two pixels) and the one-pixel gap inside it.</summary>
    public const int Inset = 3;

    public const int BannerWidth = 21;

    /// <summary>Items begin right of the banner; their icon two pixels in, their text at 67.</summary>
    public const int ItemLeft = 24, IconLeft = 26, TextLeft = 67;

    public const int ItemHeight = 32, SeparatorHeight = 8;

    /// <summary>The highlight stops 7 short of the right edge; the submenu arrow is 12 in from it.</summary>
    public const int ItemRight = 7, ArrowRight = 12;

    /// <summary>Right of the widest text, up to the menu's edge.</summary>
    public const int TextRight = 28;

    /// <summary>Lays out <paramref name="entries"/> (null for a separator), the widest text <paramref name="textWidth"/> pixels.</summary>
    public static Win98StartLayout Layout(IReadOnlyList<(string Key, string Text, bool Submenu)?> entries, int textWidth)
    {
        var items = new List<Win98StartItem>();
        var separators = new List<int>();
        int y = Inset;
        foreach ((string Key, string Text, bool Submenu)? entry in entries)
        {
            if (entry is { } item)
            {
                items.Add(new Win98StartItem(item.Key, item.Text, y, item.Submenu));
                y += ItemHeight;
            }
            else
            {
                separators.Add(y);
                y += SeparatorHeight;
            }
        }
        return new Win98StartLayout(TextLeft + textWidth + TextRight, y + Inset, items, separators);
    }

    public static ThemeCanvas Paint(Win98StartLayout layout, string? hot)
    {
        var canvas = new ThemeCanvas(layout.Width, layout.Height);
        canvas.Fill(0, 0, layout.Width, layout.Height, Face);
        RaisedFrame(canvas, 0, 0, layout.Width, layout.Height);

        // The banner: navy, its art at the bottom.
        int bannerHeight = layout.Height - 2 * Inset;
        canvas.Fill(Inset, Inset, BannerWidth, bannerHeight, Banner);
        Draw(canvas, BannerArt, Inset, layout.Height - Inset - BannerArt.Length);

        foreach (int top in layout.Separators)
        {
            canvas.Fill(IconLeft, top + 3, layout.Width - 6 - IconLeft + 1, 1, Shadow);
            canvas.Fill(IconLeft, top + 4, layout.Width - 6 - IconLeft + 1, 1, Highlight);
        }
        foreach (Win98StartItem item in layout.Items)
        {
            bool selected = item.Key == hot;
            uint text = selected ? SelectionText : Text;
            uint background = selected ? Selection : Face;
            canvas.Fill(ItemLeft, item.Top, layout.Width - ItemRight - ItemLeft + 1, ItemHeight, background);
            // Text in a 13-row line, centred in the item.
            canvas.Text(item.Text, TextLeft, item.Top + 10, layout.Width - ItemRight - TextLeft, 13, Font, text, prefix: true);
            if (item.Submenu)
                canvas.SubmenuArrow(layout.Width - ArrowRight, item.Top + 12, text);
        }
        return canvas;
    }
}
