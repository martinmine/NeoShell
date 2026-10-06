using System.Buffers.Binary;
using System.Text;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Tray;
using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class ThumbBarTests
{
    private const uint WM_USER = 0x0400;

    [Fact]
    public void Task_band_messages_76_to_78_are_the_thumbnail_toolbar()
    {
        Assert.Equal(ThumbBarCallKind.AddButtons, ThumbBarCall.KindOf(WM_USER + 76));
        Assert.Equal(ThumbBarCallKind.UpdateButtons, ThumbBarCall.KindOf(WM_USER + 77));
        Assert.Equal(ThumbBarCallKind.SetImageList, ThumbBarCall.KindOf(WM_USER + 78));
        Assert.Null(ThumbBarCall.KindOf(WM_USER + 79)); // the overlay icon
    }

    [Fact]
    public void Buttons_are_read_from_packed_records()
    {
        // As VLC adds them: previous (disabled), play and next from its image list, with tooltips on the last two.
        byte[] data = Buttons(
            (Mask: 9u, Id: 0u, Bitmap: 0, Icon: 0, Tip: "", Flags: 1u),
            (Mask: 13u, Id: 1u, Bitmap: 2, Icon: 0, Tip: "Play", Flags: 0u),
            (Mask: 15u, Id: 2u, Bitmap: 3, Icon: 0x1234, Tip: "Next", Flags: 2u));

        ThumbBarCall? call = ThumbBarCall.Parse(ThumbBarCallKind.AddButtons, 42, data, handle => new IconBitmap(1, 1, [0, 0, 0, (byte)handle]));

        Assert.NotNull(call);
        Assert.Equal(42, call.Window);
        Assert.Equal([0u, 1u, 2u], call.Buttons.Select(button => button.Id));
        Assert.Equal(ThumbButtonFlags.Disabled, call.Buttons[0].Flags);
        Assert.Equal("", call.Buttons[0].Tooltip);
        Assert.Equal("Play", call.Buttons[1].Tooltip);
        Assert.Equal(2, call.Buttons[1].Bitmap);
        Assert.Null(call.Buttons[1].Icon);           // no THB_ICON
        Assert.Equal(0x34, call.Buttons[2].Icon!.Pixels[3]);
        Assert.Equal(ThumbButtonFlags.DismissOnClick, call.Buttons[2].Flags);
    }

    [Theory]
    [InlineData(8)]  // more than the seven Windows allows
    [InlineData(-1)]
    [InlineData(3)]  // more than the data holds
    public void Button_data_that_doesnt_hold_together_is_ignored(int count)
    {
        byte[] data = Buttons((Mask: 9u, Id: 0u, Bitmap: 0, Icon: 0, Tip: "", Flags: 0u));
        BinaryPrimitives.WriteInt32LittleEndian(data, count);

        Assert.Null(ThumbBarCall.Parse(ThumbBarCallKind.AddButtons, 1, data, _ => null));
    }

    [Fact]
    public void A_32_bit_image_list_is_read_image_by_image()
    {
        // Two 2x2 images stacked in a 2x4 strip, rows bottom-up as in BMPs: the first red, the second green with half
        // alpha on its top row and none on its bottom one.
        byte[] stream = ImageList(cx: 2, cy: 2, count: 2, masked: false, Bmp(2, 4, 32, [],
            [.. G(0), .. G(0), .. G(0x80), .. G(0x80), .. R, .. R, .. R, .. R]));

        IReadOnlyList<IconBitmap>? images = ImageListStream.Read(stream);

        Assert.NotNull(images);
        Assert.Equal(2, images.Count);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, images[0].Pixels[..4]);
        Assert.Equal(new byte[] { 0, 0x80, 0, 0x80 }, images[1].Pixels[..4]);  // straight alpha, premultiplied
        Assert.Equal(0, images[1].Pixels[11]);                    // its bottom row is transparent
    }

    [Fact]
    public void A_masked_palette_image_list_takes_transparency_from_its_mask()
    {
        // One 2x2 image, 4 bits a pixel from a two-colour palette (black, blue); the mask's set bits are transparent.
        byte[] palette = [0, 0, 0, 0, 255, 0, 0, 0];
        byte[] colors = Bmp(2, 2, 4, palette, [0x11, 0, 0, 0, 0x01, 0, 0, 0]);      // bottom row blue, top row black|blue
        byte[] mask = Bmp(2, 2, 1, [0, 0, 0, 0, 255, 255, 255, 0], [0x40, 0, 0, 0, 0x00, 0, 0, 0]); // bottom right transparent
        byte[] stream = ImageList(cx: 2, cy: 2, count: 1, masked: true, [.. colors, .. mask]);

        IReadOnlyList<IconBitmap>? images = ImageListStream.Read(stream);

        Assert.NotNull(images);
        IconBitmap image = Assert.Single(images);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, image.Pixels[0..4]);     // top left: black, opaque
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels[4..8]);   // top right: blue
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels[8..12]);  // bottom left: blue
        Assert.Equal(0, image.Pixels[15]);                    // bottom right: masked out
    }

    [Fact]
    public void Anything_but_an_image_list_is_ignored()
    {
        Assert.Null(ImageListStream.Read(Encoding.ASCII.GetBytes("not an image list at all, really")));
        Assert.Null(ImageListStream.Read(ImageList(cx: 2, cy: 2, count: 5, masked: false, Bmp(2, 4, 32, [], new byte[32]))));
    }

    [Fact]
    public void Updates_change_only_the_parts_given_of_the_buttons_with_those_ids()
    {
        IconBitmap play = new(1, 1, [1, 1, 1, 1]);
        IconBitmap pause = new(1, 1, [2, 2, 2, 2]);
        ThumbBar bar = ThumbBar.Empty
            .Apply(new ThumbBarCall(ThumbBarCallKind.SetImageList, 7, [], [play, pause]))
            .Apply(new ThumbBarCall(ThumbBarCallKind.AddButtons, 7,
            [
                new(ThumbButtonMask.Bitmap | ThumbButtonMask.Tooltip, 1, 0, null, "Play", ThumbButtonFlags.Disabled),
                new(ThumbButtonMask.Flags, 2, 5, null, "ignored", ThumbButtonFlags.Hidden),
            ], []));

        Assert.Equal(ThumbButtonFlags.None, bar.Buttons[0].Flags); // flags weren't given
        Assert.Equal(-1, bar.Buttons[1].Bitmap);                    // nor was the bitmap
        Assert.Equal("", bar.Buttons[1].Tooltip);
        Assert.True(bar.Buttons[1].IsHidden);
        Assert.Same(play, bar.Buttons[0].Image(bar.Images));

        bar = bar.Apply(new ThumbBarCall(ThumbBarCallKind.UpdateButtons, 7,
            [new(ThumbButtonMask.Bitmap, 1, 1, null, "", ThumbButtonFlags.None)], []));

        Assert.Same(pause, bar.Buttons[0].Image(bar.Images));
        Assert.Equal("Play", bar.Buttons[0].Tooltip);
        Assert.True(bar.Buttons[1].IsHidden);
    }

    private static byte[] R => [0, 0, 255, 255];

    private static byte[] G(byte alpha) => [0, 255, 0, alpha];

    private static byte[] Buttons(params (uint Mask, uint Id, int Bitmap, int Icon, string Tip, uint Flags)[] buttons)
    {
        const int size = 540;
        var data = new byte[4 + buttons.Length * size];
        BinaryPrimitives.WriteInt32LittleEndian(data, buttons.Length);
        for (int i = 0; i < buttons.Length; i++)
        {
            Span<byte> button = data.AsSpan(4 + i * size, size);
            BinaryPrimitives.WriteUInt32LittleEndian(button, buttons[i].Mask);
            BinaryPrimitives.WriteUInt32LittleEndian(button[4..], buttons[i].Id);
            BinaryPrimitives.WriteInt32LittleEndian(button[8..], buttons[i].Bitmap);
            BinaryPrimitives.WriteInt32LittleEndian(button[12..], buttons[i].Icon);
            Encoding.Unicode.GetBytes(buttons[i].Tip).CopyTo(button[16..]);
            BinaryPrimitives.WriteUInt32LittleEndian(button[(size - 4)..], buttons[i].Flags);
        }
        return data;
    }

    private static byte[] ImageList(short cx, short cy, ushort count, bool masked, byte[] bitmaps)
    {
        var header = new byte[28];
        header[0] = (byte)'I';
        header[1] = (byte)'L';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 0x0620);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), count);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(10), cx);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), cy);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), (ushort)(masked ? 0x21 : 0x20));
        return [.. header, .. bitmaps];
    }

    /// <summary>A bottom-up BMP file; <paramref name="bits"/> are its rows, each padded to 4 bytes.</summary>
    private static byte[] Bmp(int width, int height, ushort bitCount, byte[] palette, IEnumerable<byte> bits)
    {
        var header = new byte[14 + 40];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), header.Length + palette.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), bitCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(46), palette.Length / 4);
        return [.. header, .. palette, .. bits];
    }
}
