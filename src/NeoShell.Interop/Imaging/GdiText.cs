using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Imaging;

/// <summary>A GDI font for <see cref="GdiText"/>: a face, its height in pixels, bold or not, italic or not.</summary>
public sealed record GdiFont(string Face, int PixelHeight, bool Bold, bool Italic = false);

/// <summary>
/// One line of text drawn by GDI straight onto opaque pixels, with grayscale antialiasing (<c>ANTIALIASED_QUALITY</c>,
/// as XP's "Standard" font smoothing), cut off with an ellipsis where it doesn't fit.
/// </summary>
public static unsafe class GdiText
{
    /// <summary>
    /// Draws <paramref name="text"/> in <paramref name="area"/> of <paramref name="pixels"/> (0xAARRGGBB rows
    /// <paramref name="width"/> long; the area comes out opaque), first in <paramref name="shadow"/> one pixel down and
    /// right if given. GDI blends each over what's below, which a white text mask composited later can't match. With
    /// <paramref name="prefix"/>, an ampersand underlines the next letter (a menu's mnemonic), as in a menu item's text.
    /// </summary>
    public static void Draw(Span<uint> pixels, int width, RectInt32 area, string text, GdiFont font, uint color, uint? shadow = null, bool prefix = false)
    {
        if (area.Width <= 0 || area.Height <= 0 || text.Length == 0)
            return;

        var header = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
            biWidth = area.Width,
            biHeight = -area.Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        nint dc = Gdi32.CreateCompatibleDC(0);
        nint bitmap = Gdi32.CreateDIBSection(dc, &header, Gdi32.DIB_RGB_COLORS, out void* bits, 0, 0);
        if (bitmap == 0)
        {
            Gdi32.DeleteDC(dc);
            return;
        }
        var buffer = new Span<uint>(bits, area.Width * area.Height);
        for (int y = 0; y < area.Height; y++)
            pixels.Slice((area.Y + y) * width + area.X, area.Width).CopyTo(buffer.Slice(y * area.Width, area.Width));
        // As GDI's own bitmaps: no alpha.
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] &= 0xFFFFFF;

        nint oldBitmap = Gdi32.SelectObject(dc, bitmap);
        nint gdiFont = Create(font);
        nint oldFont = Gdi32.SelectObject(dc, gdiFont);
        Gdi32.SetBkMode(dc, Gdi32.TRANSPARENT);
        uint format = User32.DT_SINGLELINE | User32.DT_END_ELLIPSIS | (prefix ? 0 : User32.DT_NOPREFIX);
        if (shadow is { } shadowColor)
        {
            Gdi32.SetTextColor(dc, ToColorRef(shadowColor));
            var shadowRect = new User32.RECT { left = 1, top = 1, right = area.Width, bottom = area.Height };
            User32.DrawText(dc, text, -1, ref shadowRect, format);
        }
        Gdi32.SetTextColor(dc, ToColorRef(color));
        var rect = new User32.RECT { right = area.Width - 1, bottom = area.Height };
        User32.DrawText(dc, text, -1, ref rect, format);
        Gdi32.GdiFlush();

        // GDI leaves alpha alone (zero where it drew): the result is opaque.
        for (int y = 0; y < area.Height; y++)
        {
            Span<uint> row = pixels.Slice((area.Y + y) * width + area.X, area.Width);
            buffer.Slice(y * area.Width, area.Width).CopyTo(row);
            for (int x = 0; x < row.Length; x++)
                row[x] |= 0xFF000000;
        }

        Gdi32.SelectObject(dc, oldFont);
        Gdi32.DeleteObject(gdiFont);
        Gdi32.SelectObject(dc, oldBitmap);
        Gdi32.DeleteObject(bitmap);
        Gdi32.DeleteDC(dc);
    }

    /// <summary>How wide <paramref name="text"/> is in <paramref name="font"/>, in pixels.</summary>
    public static int Measure(string text, GdiFont font)
    {
        if (text.Length == 0)
            return 0;

        nint dc = Gdi32.CreateCompatibleDC(0);
        nint gdiFont = Create(font);
        nint oldFont = Gdi32.SelectObject(dc, gdiFont);
        Gdi32.GetTextExtentPoint32(dc, text, text.Length, out User32.SIZE size);
        Gdi32.SelectObject(dc, oldFont);
        Gdi32.DeleteObject(gdiFont);
        Gdi32.DeleteDC(dc);
        return size.cx;
    }

    private static nint Create(GdiFont font) => Gdi32.CreateFont(-font.PixelHeight, 0, 0, 0, font.Bold ? Gdi32.FW_BOLD : 0,
        font.Italic ? 1u : 0, 0, 0, 1, 0, 0, Gdi32.ANTIALIASED_QUALITY, 0, font.Face);

    private static uint ToColorRef(uint argb) => ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);
}
