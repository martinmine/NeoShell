using NeoShell.Interop.Native;

namespace NeoShell.Interop.Imaging;

/// <summary>An icon as top-down 32-bit BGRA pixels with premultiplied alpha, ready for a WinUI <c>WriteableBitmap</c>.</summary>
public sealed record IconBitmap(int Width, int Height, byte[] Pixels)
{
    /// <summary>
    /// Copies an HICON's pixels. The icon itself is left alone: it may belong to another process (icon handles are
    /// valid session-wide), which can destroy it at any time after handing it over.
    /// </summary>
    public static unsafe IconBitmap? FromIcon(nint icon)
    {
        User32.ICONINFO info;
        if (!User32.GetIconInfo(icon, &info))
            return null;

        try
        {
            // Monochrome icons have no colour bitmap; nothing uses them any more.
            if (info.hbmColor == 0)
                return null;

            IconBitmap? color = Read(info.hbmColor);
            if (color is null)
                return null;

            IconBitmap? mask = info.hbmMask != 0 ? Read(info.hbmMask) : null;
            PremultiplyAlpha(color.Pixels, mask?.Width == color.Width && mask.Height == color.Height ? mask.Pixels : null);
            return color;
        }
        finally
        {
            Gdi32.DeleteObject(info.hbmColor);
            Gdi32.DeleteObject(info.hbmMask);
        }
    }

    /// <summary>Copies a 32-bit HBITMAP's pixels and deletes the bitmap.</summary>
    internal static IconBitmap? FromBitmap(nint bitmap)
    {
        try
        {
            return CopyBitmap(bitmap);
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
        }
    }

    /// <summary>Copies a 32-bit HBITMAP's pixels, leaving the bitmap to its owner (a menu's item images).</summary>
    internal static IconBitmap? CopyBitmap(nint bitmap)
    {
        IconBitmap? result = Read(bitmap);
        if (result is not null)
            PremultiplyAlpha(result.Pixels, mask: null);
        return result;
    }

    /// <summary>
    /// Brings icon pixels to premultiplied alpha. Icons come three ways: with straight alpha, already premultiplied,
    /// or with no alpha at all (all zero) and transparency in a separate AND mask, where a set mask bit means
    /// transparent. Premultiplied pixels never have a colour channel above their alpha, which tells the first two apart.
    /// </summary>
    internal static void PremultiplyAlpha(byte[] pixels, byte[]? mask)
    {
        bool hasAlpha = false;
        bool straight = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            hasAlpha |= alpha != 0;
            straight |= pixels[i] > alpha || pixels[i + 1] > alpha || pixels[i + 2] > alpha;
        }

        if (!hasAlpha)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                bool transparent = mask is not null && mask[i] != 0;
                pixels[i + 3] = transparent ? (byte)0 : (byte)255;
                if (transparent)
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
            }
        }
        else if (straight)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                pixels[i] = (byte)(pixels[i] * alpha / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * alpha / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * alpha / 255);
            }
        }
    }

    private static unsafe IconBitmap? Read(nint bitmap)
    {
        Gdi32.BITMAP header;
        if (Gdi32.GetObject(bitmap, sizeof(Gdi32.BITMAP), &header) == 0 || header.bmWidth <= 0 || header.bmHeight <= 0)
            return null;

        int width = header.bmWidth;
        int height = Math.Abs(header.bmHeight);
        var pixels = new byte[width * height * 4];
        var info = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };

        nint dc = User32.GetDC(0);
        try
        {
            fixed (byte* bits = pixels)
            {
                if (Gdi32.GetDIBits(dc, bitmap, 0, (uint)height, bits, &info, Gdi32.DIB_RGB_COLORS) == 0)
                    return null;
            }
        }
        finally
        {
            User32.ReleaseDC(0, dc);
        }
        return new IconBitmap(width, height, pixels);
    }
}
