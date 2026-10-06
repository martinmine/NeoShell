using System.Buffers.Binary;

namespace NeoShell.Interop.Imaging;

/// <summary>
/// Reads the images out of what <c>ImageList_Write</c> writes: a header (<c>ILHEAD</c>), the image strip as a BMP
/// file, and for a masked list the mask as another. Read by hand, as NeoShell doesn't load common controls 6, which
/// <c>ImageList_Read</c> needs for this format; the strip's colour depths and palettes are the BMP format's own.
/// </summary>
internal static class ImageListStream
{
    private const int HeaderSize = 28;
    private const ushort Magic = 'I' | ('L' << 8);
    private const ushort ILC_MASK = 0x0001;
    private const int BI_RGB = 0;
    private const int BI_BITFIELDS = 3;

    /// <summary>The images, top-down BGRA with premultiplied alpha; null when the data isn't an image list.</summary>
    public static IReadOnlyList<IconBitmap>? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt16LittleEndian(data) != Magic)
            return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int cx = BinaryPrimitives.ReadInt16LittleEndian(data[10..]);
        int cy = BinaryPrimitives.ReadInt16LittleEndian(data[12..]);
        bool masked = (BinaryPrimitives.ReadUInt16LittleEndian(data[18..]) & ILC_MASK) != 0;
        if (cx <= 0 || cy <= 0 || cx > 256 || cy > 256)
            return null;

        if (ReadBitmap(data[HeaderSize..], out int used) is not { } strip)
            return null;
        Bitmap? mask = masked ? ReadBitmap(data[(HeaderSize + used)..], out _) : null;

        int columns = strip.Width / cx;
        if (columns == 0 || count > columns * (strip.Height / cy))
            return null;
        var images = new List<IconBitmap>(count);
        for (int i = 0; i < count; i++)
        {
            int left = i % columns * cx;
            int top = i / columns * cy;
            byte[] pixels = strip.Crop(left, top, cx, cy);
            byte[]? maskPixels = mask is not null && mask.Width >= left + cx && mask.Height >= top + cy ? mask.Crop(left, top, cx, cy) : null;
            IconBitmap.PremultiplyAlpha(pixels, maskPixels);
            images.Add(new IconBitmap(cx, cy, pixels));
        }
        return images;
    }

    /// <summary>A BMP file's pixels as top-down BGRA; <paramref name="used"/> is how many bytes it took.</summary>
    private static Bitmap? ReadBitmap(ReadOnlySpan<byte> data, out int used)
    {
        used = 0;
        const int fileHeaderSize = 14;
        if (data.Length < fileHeaderSize + 40 || data[0] != 'B' || data[1] != 'M')
            return null;

        ReadOnlySpan<byte> info = data[fileHeaderSize..];
        int infoSize = BinaryPrimitives.ReadInt32LittleEndian(info);
        int width = BinaryPrimitives.ReadInt32LittleEndian(info[4..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(info[8..]);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(info[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(info[16..]);
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(info[32..]);
        bool bottomUp = height > 0;
        height = Math.Abs(height);
        if (infoSize < 40 || width <= 0 || height == 0 || width > 4096 || height > 4096
            || bitCount is not (1 or 4 or 8 or 24 or 32)
            || !(compression == BI_RGB || (compression == BI_BITFIELDS && bitCount == 32)))
        {
            return null;
        }

        // A palette for 8 bits or fewer; with BI_BITFIELDS, the three colour masks (always BGRA here).
        int paletteSize = bitCount <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount) * 4 : 0;
        int masksSize = compression == BI_BITFIELDS && infoSize == 40 ? 12 : 0;
        int bitsStart = fileHeaderSize + infoSize + masksSize + paletteSize;
        int stride = (width * bitCount + 31) / 32 * 4;
        if (data.Length < bitsStart + stride * height)
            return null;

        ReadOnlySpan<byte> palette = data.Slice(fileHeaderSize + infoSize + masksSize, paletteSize);
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = data.Slice(bitsStart + (bottomUp ? height - 1 - y : y) * stride, stride);
            for (int x = 0; x < width; x++)
            {
                int to = (y * width + x) * 4;
                switch (bitCount)
                {
                    case 32:
                        row.Slice(x * 4, 4).CopyTo(pixels.AsSpan(to));
                        break;
                    case 24:
                        row.Slice(x * 3, 3).CopyTo(pixels.AsSpan(to));
                        break;
                    default:
                        int perByte = 8 / bitCount;
                        int index = (row[x / perByte] >> ((perByte - 1 - x % perByte) * bitCount)) & ((1 << bitCount) - 1);
                        // A mask's palette is black and white: its white pixels are the transparent ones.
                        if (index * 4 + 3 < palette.Length)
                            palette.Slice(index * 4, 3).CopyTo(pixels.AsSpan(to));
                        break;
                }
            }
        }
        used = bitsStart + stride * height;
        return new Bitmap(width, height, pixels);
    }

    private sealed record Bitmap(int Width, int Height, byte[] Pixels)
    {
        public byte[] Crop(int left, int top, int width, int height)
        {
            var result = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                Pixels.AsSpan(((top + y) * Width + left) * 4, width * 4).CopyTo(result.AsSpan(y * width * 4));
            return result;
        }
    }
}
