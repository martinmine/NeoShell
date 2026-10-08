using NeoShell.Capture;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class CaptureTests
{
    private static readonly DateTime Taken = new(2026, 10, 5, 18, 32, 7);

    [Fact]
    public void Screenshots_are_named_after_the_time_as_windows_names_them()
    {
        Assert.Equal("Screenshot 2026-10-05 183207.png", Screenshots.FileName(Taken, _ => false));
    }

    [Fact]
    public void Screenshots_in_the_same_second_are_numbered()
    {
        string[] existing = ["Screenshot 2026-10-05 183207.png", "Screenshot 2026-10-05 183207 (2).png"];

        Assert.Equal("Screenshot 2026-10-05 183207 (3).png", Screenshots.FileName(Taken, existing.Contains));
    }

    [Fact]
    public void Snips_open_in_snipping_tools_editor_as_its_toast_opens_them()
    {
        // Snipping Tool's own toast, read from the notification database.
        Assert.Equal(
            "ms-screensketch:edit?&filePath=C%3A%5CUsers%5Cmart%5CPictures%5CScreenshots%5CScreenshot%202026-10-08%20143501.png"
                + "&isTemporary=false&saved=true&source=Toast",
            Screenshots.EditorUri(@"C:\Users\mart\Pictures\Screenshots\Screenshot 2026-10-08 143501.png", saved: true, "Toast"));
        Assert.EndsWith(
            "&isTemporary=true&saved=false&source=MarkUpButton",
            Screenshots.EditorUri(@"C:\Temp\Screenshot 2026-10-08 144534.png", saved: false, "MarkUpButton"));
    }

    // Window mode

    private static SnipTarget Window(nint handle, int x, int y, int width, int height, bool onScreen = true, int process = 10) =>
        new(handle, process, onScreen, new RectInt32(x, y, width, height));

    [Fact]
    public void Window_mode_offers_every_window_on_screen_but_the_shells()
    {
        const int shell = 1;
        SnipTarget tool = Window(1, 0, 0, 100, 100), minimized = Window(2, 0, 0, 100, 100, onScreen: false),
            taskbar = Window(3, 0, 900, 1000, 48, process: shell), sizeless = Window(4, 10, 10, 0, 0), app = Window(5, 50, 50, 300, 200);

        Assert.Equal([tool, app], SnipTargets.Choose([tool, minimized, taskbar, sizeless, app], shell));
    }

    [Fact]
    public void Window_mode_takes_the_topmost_window_under_the_pointer_and_a_pixel_around_it()
    {
        SnipTarget top = Window(1, 100, 100, 200, 100), below = Window(2, 50, 50, 400, 300);
        SnipTarget[] targets = [top, below];

        Assert.Equal(top, SnipTargets.At(targets, new PointInt32(150, 150)));
        Assert.Equal(below, SnipTargets.At(targets, new PointInt32(60, 60)));
        Assert.Equal(top, SnipTargets.At(targets, new PointInt32(99, 99)));
        Assert.Equal(top, SnipTargets.At(targets, new PointInt32(300, 200)));
        Assert.Equal(below, SnipTargets.At(targets, new PointInt32(301, 150)));
        Assert.Null(SnipTargets.At(targets, new PointInt32(20, 20)));
    }

    // Freeform mode

    private static Point[] Square(double left, double top, double size) =>
        [new(left, top), new(left + size, top), new(left + size, top + size), new(left, top + size)];

    [Fact]
    public void Freeform_smoothing_keeps_the_ends_and_rounds_the_corners_through_the_midpoints()
    {
        IReadOnlyList<Point> smoothed = FreeformPath.Smooth([new(0, 0), new(10, 0), new(10, 10)]);

        Assert.Equal(new Point(0, 0), smoothed[0]);
        Assert.Equal(new Point(10, 10), smoothed[^1]);
        Assert.Contains(new Point(5, 0), smoothed);
        Assert.Contains(new Point(10, 5), smoothed);
        // The corner itself is cut.
        Assert.DoesNotContain(new Point(10, 0), smoothed);
        Assert.All(smoothed, p => Assert.True(p.X >= 0 && p.X <= 10 && p.Y >= 0 && p.Y <= 10));
    }

    [Fact]
    public void Freeform_bounds_are_the_whole_pixels_the_path_touches_on_its_monitor()
    {
        Point[] path = [new(10.5, 20.2), new(30.7, 25), new(15, 40.1)];

        Assert.Equal(new RectInt32(10, 20, 21, 21), FreeformPath.Bounds(path, new RectInt32(0, 0, 100, 100)));
        Assert.Equal(new RectInt32(10, 20, 15, 21), FreeformPath.Bounds(path, new RectInt32(0, 0, 25, 100)));
    }

    [Fact]
    public void Freeform_mask_covers_the_inside_with_soft_edges()
    {
        var bounds = new RectInt32(0, 0, 6, 3);
        // From x 1 to 4.5: the last pixel half covered.
        byte[] mask = FreeformPath.Mask([new(1, 0), new(4.5, 0), new(4.5, 3), new(1, 3)], bounds);

        Assert.Equal<byte>([0, 255, 255, 255, 128, 0], mask[6..12]);
    }

    [Fact]
    public void Freeform_paths_that_go_round_twice_leave_a_hole_as_snipping_tools_do()
    {
        // Round a 10 pixel square and then, in the same direction, a 4 pixel one inside it.
        Point[] path = [.. Square(0, 0, 10), new(0, 0), .. Square(3, 3, 4), new(3, 3)];

        byte[] mask = FreeformPath.Mask(path, new RectInt32(0, 0, 10, 10));

        Assert.Equal(255, mask[1 * 10 + 1]);
        Assert.Equal(0, mask[5 * 10 + 5]);
        Assert.Equal(255, mask[8 * 10 + 8]);
    }

    [Fact]
    public void Freeform_snips_are_transparent_outside_the_path_and_white_there_as_bitmaps()
    {
        var picture = new IconBitmap(2, 1, [10, 20, 30, 255, 100, 150, 200, 255]);

        IconBitmap cut = FreeformPath.Cut(picture, new RectInt32(0, 0, 2, 1), [0, 128]);

        // Premultiplied, as IconBitmaps are.
        Assert.Equal<byte>([0, 0, 0, 0, 50, 75, 100, 128], cut.Pixels);
        Assert.Equal<byte>([255, 255, 255, 255, 177, 202, 227, 255], FreeformPath.OnWhite(cut).Pixels);
    }

    // Snipping Tool's settings

    [Fact]
    public void Snipping_tools_settings_are_read_from_its_values_with_their_write_times()
    {
        // As read from its settings.dat: SnippingMode 2 (window) and AutoSaveCaptures off.
        Assert.Equal(2, SnippingToolSettings.ParseInt32([0x02, 0, 0, 0, 0xCC, 0xA4, 0xBA, 0xA8, 0x22, 0x57, 0xDD, 0x01]));
        Assert.False(SnippingToolSettings.ParseBoolean([0x00, 0x2B, 0xCA, 0x6F, 0xE5, 0x22, 0x57, 0xDD, 0x01]));
        Assert.Null(SnippingToolSettings.ParseInt32(null));

        var written = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        byte[] value = SnippingToolSettings.Int32Value(4, written);
        Assert.Equal(4, SnippingToolSettings.ParseInt32(value));
        Assert.Equal(written, DateTime.FromFileTimeUtc(BitConverter.ToInt64(value, 4)));
    }
}
