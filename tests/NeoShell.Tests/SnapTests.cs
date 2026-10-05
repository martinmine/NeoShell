using NeoShell.Snap;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class SnapTests
{
    [Theory]
    [InlineData(1920, 1032, 96, 6)]  // 1920 effective pixels: thirds and the wide middle too
    [InlineData(2560, 1392, 144, 4)] // 1707 effective pixels
    [InlineData(1366, 720, 96, 4)]
    public void Wide_screens_get_more_layouts(int width, int height, uint dpi, int expected)
    {
        Assert.Equal(expected, SnapLayouts.For(new RectInt32(0, 0, width, height), dpi).Count);
    }

    [Fact]
    public void Portrait_screens_stack_the_zones()
    {
        IReadOnlyList<IReadOnlyList<SnapZone>> layouts = SnapLayouts.For(new RectInt32(0, 0, 1080, 1872), 96);

        Assert.All(layouts.SelectMany(layout => layout), zone => Assert.Equal(1, zone.Width));
    }

    [Fact]
    public void Every_layout_covers_the_work_area_exactly()
    {
        var area = new RectInt32(0, 0, 1921, 1033);
        foreach (IReadOnlyList<SnapZone> layout in SnapLayouts.For(area, 96))
            Assert.Equal((long)area.Width * area.Height, layout.Sum(zone => (long)SnapLayouts.Bounds(zone, area).Width * SnapLayouts.Bounds(zone, area).Height));
    }

    [Fact]
    public void Neighbouring_zones_share_their_edge()
    {
        var area = new RectInt32(100, 0, 1000, 700);

        RectInt32 left = SnapLayouts.Bounds(new SnapZone(0, 0, 1.0 / 3, 1), area);
        RectInt32 middle = SnapLayouts.Bounds(new SnapZone(1.0 / 3, 0, 1.0 / 3, 1), area);
        RectInt32 right = SnapLayouts.Bounds(new SnapZone(2.0 / 3, 0, 1.0 / 3, 1), area);

        Assert.Equal(new RectInt32(100, 0, 333, 700), left);
        Assert.Equal(left.X + left.Width, middle.X);
        Assert.Equal(middle.X + middle.Width, right.X);
        Assert.Equal(1100, right.X + right.Width);
    }
}
