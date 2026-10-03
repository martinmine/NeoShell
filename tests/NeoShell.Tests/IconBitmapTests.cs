using NeoShell.Interop.Imaging;

namespace NeoShell.Tests;

public sealed class IconBitmapTests
{
    [Fact]
    public void Straight_alpha_is_premultiplied()
    {
        byte[] pixels = [200, 100, 50, 128, /**/ 10, 20, 30, 255];

        IconBitmap.PremultiplyAlpha(pixels, mask: null);

        Assert.Equal([100, 50, 25, 128, /**/ 10, 20, 30, 255], pixels);
    }

    [Fact]
    public void Already_premultiplied_pixels_are_left_alone()
    {
        byte[] pixels = [64, 32, 16, 128, /**/ 0, 0, 0, 0];

        IconBitmap.PremultiplyAlpha(pixels, mask: null);

        Assert.Equal([64, 32, 16, 128, /**/ 0, 0, 0, 0], pixels);
    }

    [Fact]
    public void Icon_without_alpha_takes_transparency_from_its_mask()
    {
        byte[] pixels = [10, 20, 30, 0, /**/ 40, 50, 60, 0];
        byte[] mask = [255, 255, 255, 0, /**/ 0, 0, 0, 0]; // first pixel transparent

        IconBitmap.PremultiplyAlpha(pixels, mask);

        Assert.Equal([0, 0, 0, 0, /**/ 40, 50, 60, 255], pixels);
    }

    [Fact]
    public void Icon_without_alpha_or_mask_is_opaque()
    {
        byte[] pixels = [10, 20, 30, 0];

        IconBitmap.PremultiplyAlpha(pixels, mask: null);

        Assert.Equal([10, 20, 30, 255], pixels);
    }
}
