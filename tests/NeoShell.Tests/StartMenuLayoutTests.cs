using NeoShell.StartMenu;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class StartMenuLayoutTests
{
    private static readonly RectInt32 Monitor = new(0, 0, 1920, 1080);
    private static readonly RectInt32 Taskbar = new(0, 1032, 1920, 48);

    [Fact]
    public void Centred_above_the_taskbar_at_the_users_size()
    {
        Assert.Equal(new RectInt32(544, 160, 832, 860), StartMenuLayout.Bounds(Monitor, Taskbar, centered: true, 832, 860, 1));
    }

    [Fact]
    public void Left_aligned_starts_at_the_taskbars_left()
    {
        Assert.Equal(new RectInt32(18, 264, 900, 750), StartMenuLayout.Bounds(Monitor, Taskbar, centered: false, 600, 500, 1.5));
    }

    [Fact]
    public void Kept_on_the_monitor_and_above_a_minimum()
    {
        Assert.Equal(new RectInt32(12, 12, 1896, 1008), StartMenuLayout.Bounds(Monitor, Taskbar, centered: true, 5000, 5000, 1));
        Assert.Equal(new RectInt32(720, 620, 480, 400), StartMenuLayout.Bounds(Monitor, Taskbar, centered: true, 10, 10, 1));
    }

    [Theory]
    [InlineData(false, false, 40, -30, 872, 890)] // top-right, left-aligned: right edge follows the pointer
    [InlineData(true, true, -40, -30, 912, 890)] // top-left, centred: both sides grow
    [InlineData(false, true, -40, 20, 752, 840)] // top-right, centred, shrinking
    public void Dragging_a_top_corner(bool leftCorner, bool centered, int dx, int dy, double width, double height)
    {
        Assert.Equal((width, height), StartMenuLayout.Resize(new RectInt32(544, 160, 832, 860), dx, dy, leftCorner, centered, 1));
    }

    [Fact]
    public void Resizing_works_in_effective_pixels_and_stops_at_the_minimum()
    {
        Assert.Equal((600.0, 500.0), StartMenuLayout.Resize(new RectInt32(0, 0, 900, 750), 0, 0, false, false, 1.5));
        Assert.Equal((StartMenuLayout.MinWidth, StartMenuLayout.MinHeight), StartMenuLayout.Resize(new RectInt32(0, 0, 900, 750), -2000, 2000, false, false, 1.5));
    }
}
