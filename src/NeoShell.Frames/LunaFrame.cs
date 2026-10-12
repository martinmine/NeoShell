using NeoShell.Interop.Imaging;
using Windows.Graphics;

namespace NeoShell.Frames;

internal enum LunaKind { Sizable, Dialog, Tool }

internal enum LunaButton { Minimize, Maximize, Restore, Close, Help }

internal enum LunaButtonState { Normal, Hot, Pressed, Disabled }

/// <summary>
/// What to draw: a window <paramref name="Width"/> pixels wide (XP's window rectangle: the client area and XP's
/// borders) with a caption <paramref name="CaptionHeight"/> rows tall, its title, a 16x16 icon (premultiplied
/// 0xAARRGGBB) if any, and its caption buttons from left to right.
/// </summary>
internal sealed record LunaWindow(
    int Width,
    int CaptionHeight,
    LunaKind Kind,
    bool Active,
    bool Maximized,
    string Title,
    uint[]? Icon,
    IReadOnlyList<(LunaButton Button, LunaButtonState State)> Buttons);

/// <summary>
/// Draws XP's blue Luna frame from the numbers in <see cref="LunaArt"/> into premultiplied 0xAARRGGBB pixels: the
/// caption with its title, icon and buttons, and the borders below it. Positions are XP's at 96 DPI, relative to XP's
/// window rectangle (docs/design/window-frames-overlay.md, "Luna layout").
/// </summary>
internal static class LunaFrame
{
    public const int ButtonSize = 21;
    public const int ToolButtonSize = 13;
    // XP spaces the caption buttons 23 pixels apart.
    private const int ButtonPitch = 23;

    // The rounded top corners: pixels left transparent at each end of rows 0-4.
    private static readonly int[] s_cornerCut = [5, 3, 2, 1, 1];

    /// <summary>XP's caption, from the window's top to its client area (a maximized window's: on screen).</summary>
    public static int CaptionHeight(LunaKind kind, bool maximized) =>
        maximized ? 26 : kind switch { LunaKind.Sizable => 30, LunaKind.Dialog => 29, _ => 21 };

    public static int BorderWidth(LunaKind kind) => kind == LunaKind.Sizable ? 4 : 3;

    /// <summary>The caption buttons' rectangles, left to right, relative to the window.</summary>
    public static RectInt32[] ButtonRects(LunaWindow window)
    {
        int size = window.Kind == LunaKind.Tool ? ToolButtonSize : ButtonSize;
        // The rightmost button's last column: 6 px in from a sizable window's edge, 5 from a dialog's, 2 from the
        // screen's when maximized.
        int right = window.Maximized ? window.Width - 3 : window.Kind == LunaKind.Sizable ? window.Width - 7 : window.Width - 6;
        int top = window.Maximized ? 2 : window.Kind == LunaKind.Sizable ? 6 : 5;
        var rects = new RectInt32[window.Buttons.Count];
        for (int i = 0; i < rects.Length; i++)
            rects[i] = new RectInt32(right - size + 1 - (rects.Length - 1 - i) * ButtonPitch, top, size, size);
        return rects;
    }

    /// <summary>The caption band, <see cref="LunaWindow.Width"/> by <see cref="LunaWindow.CaptionHeight"/>.</summary>
    public static uint[] RenderCaption(LunaWindow window)
    {
        int width = window.Width, height = window.CaptionHeight;
        var pixels = new uint[width * height];
        CaptionArt art = window.Maximized ? (window.Active ? LunaArt.MaximizedActive : LunaArt.MaximizedInactive)
            : window.Kind == LunaKind.Tool ? (window.Active ? LunaArt.ToolActive : LunaArt.ToolInactive)
            : window.Active ? LunaArt.SizableActive : LunaArt.SizableInactive;
        // A dialog's caption is a sizable one without row 11.
        bool dialog = window.Kind == LunaKind.Dialog && !window.Maximized;
        int artRows = dialog ? art.Rows - 1 : art.Rows;
        for (int y = 0; y < height; y++)
        {
            int row = ArtRow(y, height, artRows, RepeatRow(window));
            if (dialog && row >= 11)
                row++;
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = Opaque(art.Pixel(x, row, width));
        }
        if (!window.Maximized && window.Kind != LunaKind.Tool)
        {
            for (int y = 0; y < Math.Min(s_cornerCut.Length, height); y++)
            {
                for (int i = 0; i < Math.Min(s_cornerCut[y], width); i++)
                {
                    pixels[y * width + i] = 0;
                    pixels[y * width + width - 1 - i] = 0;
                }
            }
        }

        RectInt32[] buttons = ButtonRects(window);
        int textX = window.Kind == LunaKind.Tool ? 3 : 7;
        if (window.Icon is { Length: 256 } icon && window.Kind != LunaKind.Tool)
        {
            // A dialog frame's icon sits 1 px further right and its title 1 px further left than a sizable frame's.
            int iconX = window.Maximized ? 2 : dialog ? 7 : 6, iconY = window.Maximized ? 4 : dialog ? 7 : 8;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    Over(pixels, width, height, iconX + x, iconY + y, icon[y * 16 + x]);
            textX = dialog ? 25 : 26;
        }
        if (window.Maximized)
            textX -= 4;
        int textRight = (buttons.Length > 0 ? buttons[0].X : width - BorderWidth(window.Kind)) - 3;
        DrawTitle(pixels, window, textX, Math.Min(textRight, width - 1));

        for (int i = 0; i < buttons.Length; i++)
            DrawButton(pixels, window, buttons[i], window.Buttons[i].Button, window.Buttons[i].State);
        return pixels;
    }

    /// <summary>
    /// The borders below the caption: the left and right ones <paramref name="sideHeight"/> rows tall (down to the
    /// bottom border), and the bottom one with its mitred corners, <see cref="LunaWindow.Width"/> wide. Opaque.
    /// </summary>
    public static (uint[] Left, uint[] Right, uint[] Bottom) RenderBorders(LunaWindow window, int sideHeight)
    {
        BorderArt art = window.Kind == LunaKind.Sizable
            ? (window.Active ? LunaArt.SizableBorderActive : LunaArt.SizableBorderInactive)
            : window.Active ? LunaArt.ThinBorderActive : LunaArt.ThinBorderInactive;
        int border = art.Width, width = window.Width;
        var left = new uint[border * Math.Max(0, sideHeight)];
        var right = new uint[left.Length];
        for (int y = 0; y < sideHeight; y++)
        {
            for (int x = 0; x < border; x++)
            {
                left[y * border + x] = Opaque(art.Left[x]);
                right[y * border + x] = Opaque(art.Right[x]);
            }
        }

        var bottom = new uint[width * border];
        for (int row = 0; row < border; row++)
        {
            for (int x = 0; x < width; x++)
            {
                // The corners' patches are one column wider than the border: they mitre into the bottom edge.
                uint color = x <= border ? art.BottomLeft[row][x]
                    : x >= width - border - 1 ? art.BottomRight[row][x - (width - border - 1)]
                    : art.Bottom[row];
                bottom[row * width + x] = Opaque(color);
            }
        }
        return (left, right, bottom);
    }

    /// <summary>
    /// An icon's premultiplied BGRA pixels as the 16x16 premultiplied 0xAARRGGBB the caption shows: averaged down
    /// from a larger size (a window that hands over only its big icon), picked nearest from a smaller one.
    /// </summary>
    public static uint[] Icon16(int width, int height, ReadOnlySpan<byte> bgra)
    {
        var icon = new uint[256];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int left = x * width / 16, top = y * height / 16;
                int right = Math.Max(left + 1, (x + 1) * width / 16), bottom = Math.Max(top + 1, (y + 1) * height / 16);
                int b = 0, g = 0, r = 0, a = 0, count = 0;
                for (int sy = top; sy < bottom; sy++)
                {
                    for (int sx = left; sx < right; sx++)
                    {
                        int i = (sy * width + sx) * 4;
                        b += bgra[i];
                        g += bgra[i + 1];
                        r += bgra[i + 2];
                        a += bgra[i + 3];
                        count++;
                    }
                }
                icon[y * 16 + x] = (uint)(((a / count) << 24) | ((r / count) << 16) | ((g / count) << 8) | (b / count));
            }
        }
        return icon;
    }

    /// <summary>
    /// The art row for row <paramref name="y"/> of a caption <paramref name="height"/> rows tall when XP's had
    /// <paramref name="artRows"/>: Windows 11's title bars are a row or three off XP's. Rows are repeated, or left out,
    /// after <paramref name="repeat"/>, one of a run of rows of one colour.
    /// </summary>
    internal static int ArtRow(int y, int height, int artRows, int repeat) =>
        y <= repeat ? y : Math.Max(repeat, y - (height - artRows));

    /// <summary>
    /// Where the caption grows or shrinks: a sizable or dialog caption below its title and buttons (in rows 22-25 of
    /// one colour), so they keep XP's gradient behind them; a maximized one in rows 6-10, above its bottom shading.
    /// </summary>
    private static int RepeatRow(LunaWindow window) =>
        window.Maximized || window.Kind == LunaKind.Tool ? 6 : window.Kind == LunaKind.Dialog ? 22 : 23;

    private static void DrawButton(uint[] pixels, LunaWindow window, RectInt32 rect, LunaButton button, LunaButtonState state)
    {
        bool tool = window.Kind == LunaKind.Tool, red = button == LunaButton.Close;
        FaceArt face = (tool, red, state, window.Active) switch
        {
            (true, _, LunaButtonState.Hot, _) => LunaArt.ToolHotFace,
            (true, _, LunaButtonState.Pressed, _) => LunaArt.ToolPressedFace,
            (true, _, _, true) => LunaArt.ToolNormalFace,
            (true, _, _, false) => LunaArt.ToolInactiveFace,
            (_, true, LunaButtonState.Hot, _) => LunaArt.RedHotFace,
            (_, true, LunaButtonState.Pressed, _) => LunaArt.RedPressedFace,
            (_, true, LunaButtonState.Normal, true) => LunaArt.RedNormalFace,
            (_, true, _, _) => LunaArt.RedInactiveFace,
            (_, _, LunaButtonState.Hot, _) => LunaArt.BlueHotFace,
            (_, _, LunaButtonState.Pressed, _) => LunaArt.BluePressedFace,
            (_, _, LunaButtonState.Disabled, true) => LunaArt.BlueDisabledFace,
            (_, _, LunaButtonState.Disabled, false) => LunaArt.BlueDisabledInactiveFace,
            (_, _, _, true) => LunaArt.BlueNormalFace,
            _ => LunaArt.BlueInactiveFace,
        };
        int size = rect.Width;
        int[] nodes = size == ToolButtonSize ? LunaArt.MeshNodes13 : LunaArt.MeshNodes21;
        string[] rim = size == ToolButtonSize ? LunaArt.Border13 : LunaArt.Border21;
        string[] glyph = tool ? LunaArt.ToolCloseGlyph : button switch
        {
            LunaButton.Minimize => LunaArt.MinimizeGlyph,
            LunaButton.Maximize => LunaArt.MaximizeGlyph,
            LunaButton.Restore => LunaArt.RestoreGlyph,
            LunaButton.Help => LunaArt.HelpGlyph,
            _ => LunaArt.CloseGlyph,
        };
        int width = window.Width, height = window.CaptionHeight;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int px = rect.X + x, py = rect.Y + y;
                if (px < 0 || px >= width || py < 0 || py >= height)
                    continue;
                int i = py * width + px;
                bool corner = (x == 1 || x == size - 2) && (y == 1 || y == size - 2);
                bool onRim = x == 0 || y == 0 || x == size - 1 || y == size - 1 || corner;
                uint color = face.At(nodes, x, y);
                if (onRim)
                {
                    // The rim is white over the caption behind the button (an inactive close button's rim is bluish,
                    // not pink); only its rounded corners' inner pixels lie over the face.
                    if (!corner)
                        color = pixels[i] & 0xFFFFFF;
                    color = White(color, Sixteenths(rim[y][x]) * face.BorderOpacity);
                }
                else
                {
                    color = White(color, Sixteenths(glyph[y][x]) * face.GlyphOpacity);
                }
                pixels[i] = Opaque(color);
            }
        }
    }

    private static void DrawTitle(uint[] pixels, LunaWindow window, int x, int right)
    {
        if (string.IsNullOrEmpty(window.Title) || right <= x)
            return;
        bool tool = window.Kind == LunaKind.Tool;
        int top = window.Maximized ? 4 : tool ? 5 : window.Kind == LunaKind.Dialog ? 7 : 8;
        if (top >= window.CaptionHeight)
            return;
        // Trebuchet MS bold 13 px (Tahoma bold 11 px on tool windows). Active: white over a 0A1883 shadow one pixel
        // down and right; inactive: D8E4F8 without one.
        var font = tool ? new GdiFont("Tahoma", 11, Bold: true) : new GdiFont("Trebuchet MS", 13, Bold: true);
        uint? shadow = window.Active && !tool ? 0xFF0A1883 : null;
        GdiText.Draw(pixels, window.Width, new RectInt32(x, top, right - x + 1, window.CaptionHeight - top), window.Title,
            font, window.Active ? 0xFFFFFFFF : 0xFFD8E4F8, shadow);
    }

    private static float Sixteenths(char alpha) => alpha == '.' ? 0 : Convert.ToInt32(alpha.ToString(), 16) / 15f;

    private static uint Opaque(uint rgb) => 0xFF000000 | rgb;

    /// <summary>White over <paramref name="color"/> at opacity <paramref name="alpha"/>.</summary>
    private static uint White(uint color, float alpha)
    {
        if (alpha <= 0)
            return color & 0xFFFFFF;
        int Channel(int shift)
        {
            int value = (int)((color >> shift) & 255);
            return value + (int)MathF.Round((255 - value) * alpha);
        }
        return (uint)((Channel(16) << 16) | (Channel(8) << 8) | Channel(0));
    }

    /// <summary>A premultiplied pixel over an opaque one.</summary>
    private static void Over(uint[] pixels, int width, int height, int x, int y, uint source)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return;
        uint alpha = source >> 24;
        if (alpha == 0)
            return;
        uint target = pixels[y * width + x];
        uint Channel(int shift) => ((source >> shift) & 255) + ((target >> shift) & 255) * (255 - alpha) / 255;
        pixels[y * width + x] = 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
