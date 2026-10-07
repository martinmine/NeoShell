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

    private const string MonitorPath = @"\\?\DISPLAY#Default_Monitor#4&427137e&0&UID0#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Fact]
    public void Explorer_per_monitor_value_gives_the_picture_and_its_monitor()
    {
        // TranscodedImageCache_000 as Explorer wrote it for img24 (header: magic, 1150704 bytes, 3840x2401, write time).
        byte[] value = new byte[800];
        Convert.FromHexString("7AC30100F08E1100000F000061090000" + "86BED75D0584DA01").CopyTo(value, 0);
        System.Text.Encoding.Unicode.GetBytes(@"C:\Windows\Web\Wallpaper\ThemeB\img24.jpg").CopyTo(value, 24);
        System.Text.Encoding.Unicode.GetBytes(MonitorPath).CopyTo(value, 544);

        Assert.Equal((@"C:\Windows\Web\Wallpaper\ThemeB\img24.jpg", MonitorPath), WallpaperRegistry.ParseImageCache(value));
    }

    [Fact]
    public void Value_for_all_monitors_has_no_monitor_and_junk_is_ignored()
    {
        byte[] value = WallpaperRegistry.BuildImageCache(@"C:\a.jpg", "", 1, new SizeInt32(1, 1), DateTime.UtcNow);

        Assert.Equal((@"C:\a.jpg", ""), WallpaperRegistry.ParseImageCache(value));
        Assert.Null(WallpaperRegistry.ParseImageCache(null));
        Assert.Null(WallpaperRegistry.ParseImageCache(new byte[800]));
        Assert.Null(WallpaperRegistry.ParseImageCache(value[..100]));
    }

    [Fact]
    public void Written_value_has_the_header_Explorer_checks()
    {
        DateTime written = DateTime.FromFileTimeUtc(0x01DA84055DD7BE86);
        byte[] value = WallpaperRegistry.BuildImageCache(
            @"C:\Windows\Web\Wallpaper\ThemeB\img24.jpg", MonitorPath, 1150704, new SizeInt32(3840, 2401), written);

        Assert.Equal(800, value.Length);
        Assert.Equal(Convert.FromHexString("7AC30100F08E1100000F000061090000" + "86BED75D0584DA01"), value[..24]);
        Assert.Equal((@"C:\Windows\Web\Wallpaper\ThemeB\img24.jpg", MonitorPath), WallpaperRegistry.ParseImageCache(value));
    }

    [Fact]
    public void Each_monitor_shows_its_own_picture_or_the_common_one()
    {
        var settings = new WallpaperSettings(@"C:\all.jpg", default, WallpaperStyle.Fill, default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MonitorPath] = @"C:\one.jpg" });

        Assert.Equal(@"C:\one.jpg", settings.ImageFor(MonitorPath.ToUpperInvariant()));
        Assert.Equal(@"C:\all.jpg", settings.ImageFor(@"\\?\DISPLAY#Other#1"));
    }

    [Fact]
    public void Settings_read_twice_are_equal()
    {
        WallpaperSettings Make() => new(@"C:\all.jpg", default, WallpaperStyle.Fill, default, new Dictionary<string, string> { [MonitorPath] = @"C:\one.jpg" });

        Assert.Equal(Make(), Make());
        Assert.NotEqual(Make(), Make() with { MonitorImages = new Dictionary<string, string>() });
    }

    [Theory]
    [InlineData(WallpaperStyle.Fill)]
    [InlineData(WallpaperStyle.Fit)]
    [InlineData(WallpaperStyle.Stretch)]
    [InlineData(WallpaperStyle.Center)]
    [InlineData(WallpaperStyle.Tile)]
    [InlineData(WallpaperStyle.Span)]
    public void Styles_are_written_as_Explorer_reads_them(WallpaperStyle style)
    {
        (string wallpaperStyle, string tile) = WallpaperSettings.FormatStyle(style);
        Assert.Equal(style, WallpaperSettings.ParseStyle(wallpaperStyle, tile));
    }

    // slideshow.ini as Explorer wrote it for C:\Windows\Web\Wallpaper\ThemeC (a 461-byte ID list).
    private const string ThemeCProfile =
        "NHAFA8BUg/E0gouOpBhoYjAArADMdmBAvMkOcBAAAAAAAAAAAAAAAAAAAAAAAAgVAEDAAAAAAYUXQRLEAcVauR2b3NHAABQCAQAAv7bgYlqOH1lxk6CAAAggOAAAAAQAAAAAAAAAAAAAAAAAAAAAXFy5AcFApBgbAQGAvBwdAMHAAAgFAoEAxAAAAAAABilT8ABAXVmYAgDAJAABA8uvBiVR7cUXCYqLAAAAHQCAAAAABAAAAAAAAAAAAAAAAAAAAQwHsBwVAUGAiBAAAIBAcBQMAAAAAAQgY5egQAwVhxGbwFGclJHAEBQCAQAAv7bgYV0OH1V8l6CAAAQDkAAAAAQAAAAAAAAAAAAAAAAAAAAAlDCMBcFAhBAbAwGAwBQYAAHAlBgcAAAAYAgoAEDAAAAAAEIWMtTEAQFal1WZDBAAMCQCAQAAv7bgYV0OH11Kn6CAAAgEkAAAAAQAAAAAAAAAAAAA8AAAAAAAkbUBBQFAoBQZA0GAlBwQAAAAABwQAoDAcBwVAkEAOBARA8EAXBwUAwFATBQeAMHA0BQZA0GAzAgMAwFA0BAaAUGAtBQZAUHApBgLAQGAsBAbAwCAtAgMAEDAxAgNAAAAWAAAAA";

    [Fact]
    public void Profile_binary_is_Base64_with_the_lowest_bits_first()
    {
        byte[] bytes = WallpaperRegistry.DecodeProfileBinary(ThemeCProfile)!;

        // The ID list's size, then This PC's item (20 bytes, root folder GUID 20D04FE0-…).
        Assert.Equal(461, BitConverter.ToUInt16(bytes));
        Assert.Equal(Convert.FromHexString("14001F50E04FD020EA3A6910A2D808002B30309D"), bytes[2..22]);
        Assert.Equal([0, 0], bytes[461..463]);
        Assert.Equal(ThemeCProfile, WallpaperRegistry.EncodeProfileBinary(bytes[..463]));
        Assert.Null(WallpaperRegistry.DecodeProfileBinary("AB*C"));
    }

    private static IReadOnlyList<RectInt32> Arrange(WallpaperStyle style, int width, int height) =>
        WallpaperLayout.Arrange(style, new SizeInt32(width, height), Monitor, Monitor);
}
