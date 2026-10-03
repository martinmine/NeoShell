using NeoShell.Desktop;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Tests;

public sealed class WallpaperTests
{
    private static readonly RectInt32 Monitor = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("10", "0", WallpaperStyle.Fill)]
    [InlineData("6", "0", WallpaperStyle.Fit)]
    [InlineData("2", "0", WallpaperStyle.Stretch)]
    [InlineData("0", "0", WallpaperStyle.Center)]
    [InlineData("0", null, WallpaperStyle.Center)]
    [InlineData("0", "1", WallpaperStyle.Tile)]
    [InlineData("22", "0", WallpaperStyle.Span)]
    [InlineData(" 6 ", "0", WallpaperStyle.Fit)]
    [InlineData("10", "1", WallpaperStyle.Fill)] // TileWallpaper only matters with style 0
    [InlineData(null, null, WallpaperStyle.Fill)]
    [InlineData("99", "0", WallpaperStyle.Fill)]
    public void Registry_style_values_map_to_styles(string? wallpaperStyle, string? tileWallpaper, WallpaperStyle expected)
    {
        Assert.Equal(expected, WallpaperSettings.ParseStyle(wallpaperStyle, tileWallpaper));
    }

    [Theory]
    [InlineData("0 99 188", 0, 99, 188)]
    [InlineData("  255   255 255 ", 255, 255, 255)]
    [InlineData(null, 0, 0, 0)]
    [InlineData("", 0, 0, 0)]
    [InlineData("1 2", 0, 0, 0)]
    [InlineData("1 2 300", 0, 0, 0)]
    [InlineData("red", 0, 0, 0)]
    public void Background_colour_is_parsed_from_rgb(string? value, byte r, byte g, byte b)
    {
        Assert.Equal(new Color { A = 255, R = r, G = g, B = b }, WallpaperSettings.ParseColor(value));
    }

    [Fact]
    public void Fill_covers_the_monitor_and_crops_the_overflow_evenly()
    {
        // 4:3 image on a 16:9 monitor: scaled to the full width, cropped top and bottom.
        Assert.Equal([new RectInt32(0, -180, 1920, 1440)], Arrange(WallpaperStyle.Fill, 800, 600));
    }

    [Fact]
    public void Fit_shows_the_whole_image_with_bars()
    {
        Assert.Equal([new RectInt32(240, 0, 1440, 1080)], Arrange(WallpaperStyle.Fit, 800, 600));
    }

    [Fact]
    public void Stretch_covers_the_monitor_exactly()
    {
        Assert.Equal([new RectInt32(0, 0, 1920, 1080)], Arrange(WallpaperStyle.Stretch, 800, 600));
    }

    [Fact]
    public void Center_keeps_the_image_size()
    {
        Assert.Equal([new RectInt32(560, 240, 800, 600)], Arrange(WallpaperStyle.Center, 800, 600));
        Assert.Equal([new RectInt32(-1040, -540, 4000, 2160)], Arrange(WallpaperStyle.Center, 4000, 2160));
    }

    [Fact]
    public void Tile_repeats_the_image_from_the_top_left_corner()
    {
        IReadOnlyList<RectInt32> tiles = Arrange(WallpaperStyle.Tile, 1000, 700);

        Assert.Equal(
            [new(0, 0, 1000, 700), new(1000, 0, 1000, 700), new(0, 700, 1000, 700), new(1000, 700, 1000, 700)],
            tiles);
    }

    [Fact]
    public void Tile_with_a_tiny_image_falls_back_to_fill()
    {
        Assert.Equal([new RectInt32(0, -420, 1920, 1920)], Arrange(WallpaperStyle.Tile, 2, 2));
    }

    [Fact]
    public void Span_fills_the_virtual_screen_and_each_monitor_shows_its_part()
    {
        var left = new RectInt32(-1920, 0, 1920, 1080);
        var right = new RectInt32(0, 0, 1920, 1080);
        RectInt32 virtualScreen = WallpaperLayout.Union([left, right]);
        var image = new SizeInt32(3840, 1080);

        Assert.Equal([new RectInt32(0, 0, 3840, 1080)], WallpaperLayout.Arrange(WallpaperStyle.Span, image, left, virtualScreen));
        Assert.Equal([new RectInt32(-1920, 0, 3840, 1080)], WallpaperLayout.Arrange(WallpaperStyle.Span, image, right, virtualScreen));
    }

    [Fact]
    public void Empty_image_draws_nothing()
    {
        Assert.Empty(Arrange(WallpaperStyle.Fill, 0, 0));
    }

    [Fact]
    public void Union_is_the_bounding_box_of_all_monitors()
    {
        RectInt32 union = WallpaperLayout.Union([new(0, 0, 1920, 1080), new(1920, -200, 2560, 1440)]);

        Assert.Equal(new RectInt32(0, -200, 4480, 1440), union);
        Assert.Equal(default, WallpaperLayout.Union([]));
    }

    private static IReadOnlyList<RectInt32> Arrange(WallpaperStyle style, int width, int height) =>
        WallpaperLayout.Arrange(style, new SizeInt32(width, height), Monitor, Monitor);
}
