using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NeoShell.Interop.Imaging;

/// <summary>Decodes pictures (any format Windows has a codec for), upright as their EXIF orientation says.</summary>
public static class Pictures
{
    /// <summary>The picture's size, upright; reads little more than its header. Throws if it can't be decoded.</summary>
    public static async Task<SizeInt32> GetSizeAsync(byte[] bytes)
    {
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(await ToStreamAsync(bytes));
        return new SizeInt32((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
    }

    /// <summary>
    /// Decodes the picture scaled to <paramref name="size"/> (upright), as top-down premultiplied BGRA pixels. Throws if
    /// it can't be decoded.
    /// </summary>
    public static async Task<IconBitmap> DecodeAsync(byte[] bytes, SizeInt32 size)
    {
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(await ToStreamAsync(bytes));
        // The scale applies before the rotation: a turned picture is scaled to the turned size.
        bool turned = decoder.OrientedPixelWidth != decoder.PixelWidth;
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)(turned ? size.Height : size.Width),
            ScaledHeight = (uint)(turned ? size.Width : size.Height),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        return new IconBitmap(size.Width, size.Height, pixels.DetachPixelData());
    }

    private static async Task<IRandomAccessStream> ToStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        return stream;
    }
}
