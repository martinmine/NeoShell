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

    private static readonly RectInt32 WorkArea = new(0, 0, 1600, 900); // corners: 112 pixels

    [Theory]
    [InlineData(0, 450, SnapPosition.Left)]
    [InlineData(1599, 450, SnapPosition.Right)]
    [InlineData(800, 0, SnapPosition.Maximized)]
    [InlineData(0, 50, SnapPosition.TopLeft)]
    [InlineData(1, 880, SnapPosition.BottomLeft)]
    [InlineData(1599, 20, SnapPosition.TopRight)]
    [InlineData(1598, 899, SnapPosition.BottomRight)]
    [InlineData(60, 0, SnapPosition.TopLeft)]       // the top edge near a corner
    [InlineData(1550, 0, SnapPosition.TopRight)]
    [InlineData(5, 450, SnapPosition.None)]         // off the edge
    [InlineData(800, 450, SnapPosition.None)]
    [InlineData(800, 899, SnapPosition.None)]       // the bottom edge (the taskbar) doesn't snap
    public void A_window_snaps_where_the_pointer_pushes_against_an_edge(int x, int y, SnapPosition expected)
    {
        Assert.Equal(expected, WindowSnap.AtPointer(new PointInt32(x, y), WorkArea, 96));
    }

    [Fact]
    public void Edges_are_the_work_areas_on_any_monitor()
    {
        // A monitor right of the first, above it, with a sidebar of 320 pixels on its right.
        var workArea = new RectInt32(1600, -200, 1600, 1032);

        Assert.Equal(SnapPosition.Left, WindowSnap.AtPointer(new PointInt32(1600, 300), workArea, 96));
        Assert.Equal(SnapPosition.Maximized, WindowSnap.AtPointer(new PointInt32(2400, -200), workArea, 96));
        Assert.Equal(SnapPosition.Right, WindowSnap.AtPointer(new PointInt32(3199, 300), workArea, 96));
    }

    [Theory]
    [InlineData(SnapPosition.None, SnapKey.Left, SnapPosition.Left)]
    [InlineData(SnapPosition.Maximized, SnapKey.Left, SnapPosition.Left)]
    [InlineData(SnapPosition.Right, SnapKey.Left, SnapPosition.None)]
    [InlineData(SnapPosition.TopRight, SnapKey.Left, SnapPosition.TopLeft)]
    [InlineData(SnapPosition.Left, SnapKey.Right, SnapPosition.None)]
    [InlineData(SnapPosition.BottomLeft, SnapKey.Right, SnapPosition.BottomRight)]
    [InlineData(SnapPosition.None, SnapKey.Up, SnapPosition.Maximized)]
    [InlineData(SnapPosition.Left, SnapKey.Up, SnapPosition.TopLeft)]
    [InlineData(SnapPosition.BottomRight, SnapKey.Up, SnapPosition.Right)]
    [InlineData(SnapPosition.Maximized, SnapKey.Down, SnapPosition.None)]
    [InlineData(SnapPosition.Right, SnapKey.Down, SnapPosition.BottomRight)]
    [InlineData(SnapPosition.TopLeft, SnapKey.Down, SnapPosition.Left)]
    [InlineData(SnapPosition.None, SnapKey.Down, SnapPosition.Minimized)]
    [InlineData(SnapPosition.BottomLeft, SnapKey.Down, SnapPosition.Minimized)]
    public void Win_arrows_move_between_halves_quarters_and_maximized(SnapPosition from, SnapKey key, SnapPosition to)
    {
        Assert.Equal(to, WindowSnap.AfterKey(from, key));
    }

    [Fact]
    public void Halves_and_quarters_tile_the_work_area()
    {
        var area = new RectInt32(0, 0, 1601, 853);
        SnapPosition[] quarters = [SnapPosition.TopLeft, SnapPosition.TopRight, SnapPosition.BottomLeft, SnapPosition.BottomRight];
        Assert.Equal((long)area.Width * area.Height, quarters.Sum(q => Area(SnapLayouts.Bounds(WindowSnap.Zone(q)!, area))));
        Assert.Equal((long)area.Width * area.Height,
            Area(SnapLayouts.Bounds(WindowSnap.Zone(SnapPosition.Left)!, area)) + Area(SnapLayouts.Bounds(WindowSnap.Zone(SnapPosition.Right)!, area)));
        Assert.Null(WindowSnap.Zone(SnapPosition.Maximized));

        static long Area(RectInt32 rect) => (long)rect.Width * rect.Height;
    }

    [Fact]
    public void A_snapped_window_dragged_away_gets_its_size_back_under_the_pointer()
    {
        // Grabbed three quarters along a 800 wide half, let go at x 1000: the 400 wide window keeps that share.
        RectInt32 unsnapped = WindowSnap.Unsnapped(new RectInt32(400, 120, 800, 1000), new RectInt32(50, 60, 400, 300), 1000, 0.75);

        Assert.Equal(new RectInt32(700, 120, 400, 300), unsnapped);
    }

    [Fact]
    public void Win_arrow_snaps_once_per_press_and_the_arrow_is_swallowed()
    {
        var keys = new SnapKeys();

        Assert.False(keys.OnKey(0x5B, true, out _));
        Assert.True(keys.OnKey(0x25, true, out SnapKey? key));
        Assert.Equal(SnapKey.Left, key);
        Assert.True(keys.OnKey(0x25, true, out key)); // held: swallowed, not snapped again
        Assert.Null(key);
        Assert.True(keys.OnKey(0x25, false, out _));
        Assert.True(keys.OnKey(0x28, true, out key));
        Assert.Equal(SnapKey.Down, key);
    }

    [Fact]
    public void Arrows_without_Win_or_with_other_modifiers_pass()
    {
        var keys = new SnapKeys();

        Assert.False(keys.OnKey(0x26, true, out SnapKey? key)); // an arrow on its own
        keys.OnKey(0x26, false, out _);
        keys.OnKey(0x5C, true, out _);
        keys.OnKey(0xA0, true, out _);                            // Win+Shift+Left moves to another monitor
        Assert.False(keys.OnKey(0x25, true, out key));
        Assert.Null(key);
    }
}
