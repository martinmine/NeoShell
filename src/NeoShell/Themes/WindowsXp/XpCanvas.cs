using NeoShell.Interop.Imaging;
using Windows.Graphics;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// Pixels the XP theme draws into: premultiplied 0xAARRGGBB, as a WriteableBitmap takes them. Text goes through GDI
/// with grayscale antialiasing, as XP drew it ("Standard" font smoothing), onto what's already drawn.
/// </summary>
public sealed class XpCanvas(int width, int height)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public uint[] Pixels { get; } = new uint[width * height];

    public uint this[int x, int y] => Pixels[y * Width + x];

    /// <summary>An opaque colour (RRGGBB), outside the canvas ignored.</summary>
    public void Set(int x, int y, uint rgb)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height)
            Pixels[y * Width + x] = 0xFF000000 | rgb;
    }

    public void Fill(int x, int y, int width, int height, uint rgb)
    {
        for (int row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (int column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
                Pixels[row * Width + column] = 0xFF000000 | rgb;
        }
    }

    /// <summary>Makes a rectangle see-through.</summary>
    public void Clear(int x, int y, int width, int height)
    {
        for (int row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (int column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
                Pixels[row * Width + column] = 0;
        }
    }

    /// <summary>Each row of a rectangle in its own colour.</summary>
    public void FillRows(int x, int y, int width, IReadOnlyList<uint> rows)
    {
        for (int row = 0; row < rows.Count; row++)
            Fill(x, y + row, width, 1, rows[row]);
    }

    /// <summary>A stretching piece of art, <paramref name="width"/> wide.</summary>
    public void Band(XpBand band, int x, int y, int width)
    {
        for (int row = 0; row < band.Rows; row++)
        {
            for (int column = 0; column < width; column++)
                Set(x + column, y + row, band.Pixel(column, row, width));
        }
    }

    /// <summary>Measured pixels (RRGGBB rows), opaque.</summary>
    public void Patch(uint[][] rows, int x, int y)
    {
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
                Set(x + column, y + row, rows[row][column]);
        }
    }

    /// <summary>Measured pixels with their opacity (AARRGGBB rows, not premultiplied), over what's drawn.</summary>
    public void PatchOver(uint[][] rows, int x, int y)
    {
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                uint argb = rows[row][column];
                Blend(x + column, y + row, argb & 0xFFFFFF, (argb >> 24) / 255.0);
            }
        }
    }

    /// <summary>A colour over the pixel at <paramref name="opacity"/>; a see-through pixel takes it premultiplied.</summary>
    public void Blend(int x, int y, uint rgb, double opacity)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || opacity <= 0)
            return;

        uint below = Pixels[y * Width + x];
        double a = Math.Min(1, opacity);
        uint Channel(int shift) =>
            (uint)Math.Round(((rgb >> shift) & 255) * a + ((below >> shift) & 255) * (1 - a));
        uint alpha = (uint)Math.Round(255 * a + (below >> 24) * (1 - a));
        Pixels[y * Width + x] = (alpha << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    /// <summary>
    /// One line of text whose box starts at (<paramref name="x"/>, <paramref name="y"/>), cut off with an ellipsis at
    /// <paramref name="width"/>. The box must be opaque: GDI draws onto it.
    /// </summary>
    public void Text(string text, int x, int y, int width, int height, GdiFont font, uint rgb)
    {
        RectInt32 area = Clip(x, y, width, height);
        if (area.Width > 0 && area.Height > 0)
            GdiText.Draw(Pixels, Width, area with { }, text, font, rgb);
    }

    /// <summary>
    /// Text over a soft shadow, as XP's themes drew Start's "start" and the user's name: the text's shape blurred
    /// (weights 1 2 3 2 1 each way), moved one pixel down and right, in <paramref name="shadow"/> at <paramref name="opacity"/>.
    /// </summary>
    public void ShadowedText(string text, int x, int y, int width, int height, GdiFont font, uint rgb, uint shadow, double opacity)
    {
        float[] mask = Coverage(text, width, height, font);
        float[] blurred = Blur(mask, width, height);
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
                Blend(x + column + 1, y + row + 1, shadow, blurred[row * width + column] * opacity);
        }
        Text(text, x, y, width, height, font, rgb);
    }

    // How much of each pixel the text covers: white text drawn on black.
    private static float[] Coverage(string text, int width, int height, GdiFont font)
    {
        var mask = new XpCanvas(width, height);
        mask.Fill(0, 0, width, height, 0);
        mask.Text(text, 0, 0, width, height, font, 0xFFFFFF);
        return [.. mask.Pixels.Select(pixel => ((pixel >> 8) & 255) / 255f)];
    }

    private static float[] Blur(float[] source, int width, int height)
    {
        ReadOnlySpan<float> weights = [1 / 9f, 2 / 9f, 3 / 9f, 2 / 9f, 1 / 9f];
        var across = new float[source.Length];
        var result = new float[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int i = 0; i < weights.Length; i++)
                    sum += source[y * width + Math.Clamp(x + i - 2, 0, width - 1)] * weights[i];
                across[y * width + x] = sum;
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int i = 0; i < weights.Length; i++)
                    sum += across[Math.Clamp(y + i - 2, 0, height - 1) * width + x] * weights[i];
                result[y * width + x] = sum;
            }
        }
        return result;
    }

    private RectInt32 Clip(int x, int y, int width, int height)
    {
        int left = Math.Max(0, x), top = Math.Max(0, y);
        int right = Math.Min(Width, x + width), bottom = Math.Min(Height, y + height);
        return new RectInt32(left, top, right - left, bottom - top);
    }
}
