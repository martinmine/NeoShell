using System.Buffers.Binary;
using System.Text.Json.Nodes;
using NeoShell.Agents;
using NeoShell.Interop.Imaging;

namespace NeoShell.Tests;

public sealed class ScreenToolsTests
{
    [Theory]
    [InlineData(1920, 1080, 1568, 1568, 882)]
    [InlineData(1080, 1920, 1568, 882, 1568)]
    [InlineData(800, 600, 1568, 800, 600)]
    [InlineData(5000, 1, 100, 100, 1)]
    public void A_picture_is_scaled_down_by_its_longest_side(int width, int height, int max, int scaledWidth, int scaledHeight) =>
        Assert.Equal((scaledWidth, scaledHeight), ScreenTools.ScaledSize(width, height, max));

    [Fact]
    public void A_crop_takes_the_rows_of_its_part_and_stays_inside()
    {
        // 3×2 pixels numbered 0..5 in their blue byte.
        byte[] pixels = new byte[3 * 2 * 4];
        for (int i = 0; i < 6; i++)
            pixels[i * 4] = (byte)i;
        var image = new IconBitmap(3, 2, pixels);

        IconBitmap part = ScreenCapture.Crop(image, 1, 0, 2, 2);
        Assert.Equal((2, 2), (part.Width, part.Height));
        Assert.Equal([1, 2, 4, 5], Enumerable.Range(0, 4).Select(i => (int)part.Pixels[i * 4]));

        IconBitmap clamped = ScreenCapture.Crop(image, -5, 1, 10, 10);
        Assert.Equal((3, 1), (clamped.Width, clamped.Height));
    }

    [Fact]
    public async Task A_scaled_PNG_has_the_size_asked_for()
    {
        var image = new IconBitmap(40, 20, Enumerable.Repeat((byte)200, 40 * 20 * 4).ToArray());

        byte[] png = await ScreenCapture.EncodeScaledPngAsync(image, 10, 5);

        // The IHDR chunk: width and height, big-endian, after the 8-byte signature and the chunk's length and type.
        Assert.Equal(0x89, png[0]);
        Assert.Equal(10, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Fact]
    public async Task A_picture_goes_before_the_text_as_image_content()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        var tool = new AgentTool("shot", AgentToolGroup.Screen, "Shoots.", AgentTool.NoArguments(), _ => new ToolResult("1×1", Png: png));
        var session = new McpSession([tool], _ => true, handler => handler(), TimeSpan.FromSeconds(5), "1");

        var answer = (JsonObject)JsonNode.Parse((await session.HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"shot"}}"""))!)!;

        JsonArray content = (JsonArray)answer["result"]!["content"]!;
        Assert.Equal("image", (string)content[0]!["type"]!);
        Assert.Equal("image/png", (string)content[0]!["mimeType"]!);
        Assert.Equal(png, Convert.FromBase64String((string)content[0]!["data"]!));
        Assert.Equal("1×1", (string)content[1]!["text"]!);
    }
}
