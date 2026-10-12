using NeoShell.Interop.Imaging;

namespace NeoShell.Themes.WindowsXp;

public enum XpStartColumn { Left, Right }

/// <summary>An item of XP's Start: in the white left column (pinned or most used programs) or the blue right one (places).</summary>
public sealed record XpStartItem(string Key, string Text, bool Bold, XpStartColumn Column, int Top, int Height, string? Subtitle = null);

/// <summary>Where everything of XP's Start goes, from its top left, in pixels at 96 DPI.</summary>
public sealed record XpStartLayout(int Height, IReadOnlyList<XpStartItem> Items, IReadOnlyList<int> LeftSeparators,
    IReadOnlyList<int> RightSeparators, int AllProgramsTop, int BottomTop);

/// <summary>
/// XP's two-column Start menu, as measured (docs/design/theme-xp.md): the blue header with the user's picture and name,
/// the orange line, the left column of programs above All Programs, the right column of places, the bottom bar with Log
/// Off and Turn Off Computer, and the shadow.
/// </summary>
public static class XpStartMenuPainter
{
    public const int Width = 380;
    public const int Shadow = 5;
    public const int HeaderHeight = 65;
    public const int BottomHeight = 41;

    // The columns: items 73 below the top, the left's 36 tall and 40 apart, the right's 26 tall and 30 apart, groups
    // parted by an 8-pixel separator (its line 3 down); All Programs 24 tall, 7 below the left's last line, 5 above the
    // bottom bar.
    private const int ItemsTop = 73;
    private const int LeftPitch = 40, LeftHeight = 36;
    private const int RightPitch = 30, RightHeight = 26;
    private const int AllProgramsHeight = 24;

    public const int LeftHighlightX = 8, LeftHighlightWidth = 176, LeftTextX = 46, LeftIconX = 9;
    public const int RightHighlightX = 196, RightHighlightWidth = 176, RightTextX = 226, RightIconX = 198;
    public const int AllProgramsWidth = 137;

    private const uint Highlight = 0x316AC5;
    private const uint LeftText = 0x373738;
    private const uint RightText = 0x0A246A;
    private const uint RightBackground = 0xD3E5FA;

    /// <param name="pinned">The pinned programs, at the top of the left column, each with a grey line under it if it has one.</param>
    /// <param name="mostUsed">The most used programs, below them.</param>
    /// <param name="places">The right column's groups of places, each item's text and whether it's bold.</param>
    public static XpStartLayout Layout(IReadOnlyList<(string Text, string? Subtitle)> pinned, IReadOnlyList<string> mostUsed,
        IReadOnlyList<IReadOnlyList<(string Key, string Text, bool Bold)>> places)
    {
        var items = new List<XpStartItem>();
        var leftSeparators = new List<int>();
        int y = ItemsTop;
        for (int i = 0; i < pinned.Count; i++, y += LeftPitch)
            items.Add(new XpStartItem($"pinned:{i}", pinned[i].Text, true, XpStartColumn.Left, y, LeftHeight, pinned[i].Subtitle));
        if (pinned.Count > 0)
        {
            leftSeparators.Add(y + 3);
            y += 8;
        }
        for (int i = 0; i < mostUsed.Count; i++, y += LeftPitch)
            items.Add(new XpStartItem($"mfu:{i}", mostUsed[i], false, XpStartColumn.Left, y, LeftHeight));
        leftSeparators.Add(y);
        int leftBottom = y + 7 + AllProgramsHeight + 5;

        var rightSeparators = new List<int>();
        int right = ItemsTop;
        for (int group = 0; group < places.Count; group++)
        {
            if (group > 0)
            {
                rightSeparators.Add(right + 3);
                right += 8;
            }
            foreach ((string key, string text, bool bold) in places[group])
            {
                items.Add(new XpStartItem(key, text, bold, XpStartColumn.Right, right, RightHeight));
                right += RightPitch;
            }
        }

        // The longer column sets the height; All Programs stays at the bottom of the left one.
        int bottom = Math.Max(leftBottom, right + 4);
        if (bottom > leftBottom)
            leftSeparators[^1] = bottom - 5 - AllProgramsHeight - 7;
        return new XpStartLayout(bottom + BottomHeight, items, leftSeparators, rightSeparators, bottom - 5 - AllProgramsHeight, bottom);
    }

    /// <param name="hot">The item under the pointer (its key, "allprograms", "logoff" or "turnoff"), or null.</param>
    public static XpCanvas Paint(XpStartLayout layout, string userName, string? hot)
    {
        int height = layout.Height;
        var canvas = new XpCanvas(Width + Shadow, height + Shadow);
        XpPainting.DropShadow(canvas, 0, 0, Width, height);

        // Header, its top corners rounded.
        canvas.Band(XpArt.StartHeader, 0, 0, Width);
        for (int row = 0; row < XpArt.StartCorner.Length; row++)
        {
            canvas.Clear(0, row, XpArt.StartCorner[row], 1);
            canvas.Clear(Width - XpArt.StartCorner[row], row, XpArt.StartCorner[row], 1);
        }
        canvas.Patch(XpArt.PictureFrame, 4, 2);
        canvas.ShadowedText(userName, 67, 20, Width - 80, 30, XpArt.UserNameFont, XpArt.White, 0x000000, 0.5);
        for (int row = 0; row < 2; row++)
        {
            for (int x = 0; x < Width; x++)
                canvas.Set(x, HeaderHeight + row, XpArt.StartOrangeLine[x]);
        }

        // The two columns, their borders and the right column's shaded edge.
        int bodyTop = HeaderHeight + 2, bodyHeight = layout.BottomTop - bodyTop;
        canvas.Fill(0, bodyTop, Width, bodyHeight, 0xFFFFFF);
        canvas.Fill(191, bodyTop, Width - 191, bodyHeight, RightBackground);
        uint[] edge = [0x1854C2, 0x91A4DA];
        uint[] rightEdge = [0xD2E4FA, 0xCFE2F8, 0xC5DAF4, 0xB8D0EE, 0xA6C2E6, 0x2B6DD1, 0x1854C2];
        for (int i = 0; i < edge.Length; i++)
            canvas.Fill(i, bodyTop, 1, bodyHeight, edge[i]);
        for (int i = 0; i < rightEdge.Length; i++)
            canvas.Fill(Width - rightEdge.Length + i, bodyTop, 1, bodyHeight, rightEdge[i]);
        canvas.Fill(190, bodyTop, 1, bodyHeight, 0x95BDEE);
        foreach (int y in layout.LeftSeparators)
            XpPainting.FadingLine(canvas, 20, 178, y);
        foreach (int y in layout.RightSeparators)
            XpPainting.FadingLine(canvas, 216, 362, y, RightBackground, 0x81B6FF, 0xEDEDED);

        foreach (XpStartItem item in layout.Items)
            PaintItem(canvas, item, item.Key == hot);

        bool allHot = hot == "allprograms";
        if (allHot)
            canvas.Fill(LeftHighlightX, layout.AllProgramsTop, AllProgramsWidth, AllProgramsHeight, Highlight);
        canvas.Text("All Programs", LeftTextX, layout.AllProgramsTop + 5, 80, 13, XpArt.TahomaBold, allHot ? XpArt.White : LeftText);
        canvas.Patch(allHot ? XpArt.AllProgramsArrowHot : XpArt.AllProgramsArrow, 129, layout.AllProgramsTop + 1);

        // The bottom bar.
        canvas.Band(XpArt.StartBottom, 0, layout.BottomTop, Width);
        canvas.Patch(XpArt.LogOffIcon, 176, layout.BottomTop + 8);
        canvas.Patch(XpArt.TurnOffIcon, 251, layout.BottomTop + 8);
        canvas.Text("Log Off", 204, layout.BottomTop + 13, 45, 13, XpArt.Tahoma, XpArt.White);
        canvas.Text("Turn Off Computer", 279, layout.BottomTop + 13, 95, 13, XpArt.Tahoma, XpArt.White);
        return canvas;
    }

    private static void PaintItem(XpCanvas canvas, XpStartItem item, bool hot)
    {
        bool left = item.Column == XpStartColumn.Left;
        if (hot)
        {
            canvas.Fill(left ? LeftHighlightX : RightHighlightX, item.Top,
                left ? LeftHighlightWidth : RightHighlightWidth, item.Height, Highlight);
        }
        uint color = hot ? XpArt.White : left ? LeftText : RightText;
        GdiFont font = item.Bold ? XpArt.TahomaBold : XpArt.Tahoma;
        if (left)
        {
            if (item.Subtitle is { } subtitle)
            {
                canvas.Text(item.Text, LeftTextX, item.Top + 5, 183 - LeftTextX, 13, font, color);
                canvas.Text(subtitle, LeftTextX, item.Top + 18, 183 - LeftTextX, 13, XpArt.Tahoma, hot ? XpArt.White : 0x808080);
                return;
            }
            // One line in the middle of the item, or two (a long name wrapped) from 5 down.
            IReadOnlyList<string> lines = Lines(item.Text, font, 183 - LeftTextX - 4);
            int top = lines.Count == 1 ? item.Top + 11 : item.Top + 5;
            for (int i = 0; i < lines.Count; i++)
                canvas.Text(lines[i], LeftTextX, top + i * 13, 183 - LeftTextX, 13, font, color);
        }
        else
        {
            canvas.Text(item.Text, RightTextX, item.Top + 6, 371 - RightTextX, 13, font, color);
        }
    }

    // A name broken between words into at most two lines that fit, as XP wrapped long program names.
    private static IReadOnlyList<string> Lines(string text, GdiFont font, int width)
    {
        if (GdiText.Measure(text, font) <= width)
            return [text];
        string[] words = text.Split(' ');
        for (int count = words.Length - 1; count > 0; count--)
        {
            string first = string.Join(' ', words[..count]);
            if (GdiText.Measure(first, font) <= width)
                return [first, string.Join(' ', words[count..])];
        }
        return [text];
    }
}
