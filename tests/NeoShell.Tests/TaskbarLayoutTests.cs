using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;
using NeoShell;
using NeoShell.Taskbar;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class TaskbarLayoutTests
{
    [Theory]
    [InlineData(96u, 48)]   // 100%
    [InlineData(120u, 60)]  // 125%
    [InlineData(144u, 72)]  // 150%
    [InlineData(168u, 84)]  // 175%
    [InlineData(192u, 96)]  // 200%
    public void Height_scales_with_dpi(uint dpi, int expected)
    {
        Assert.Equal(expected, TaskbarLayout.PhysicalHeight(dpi));
    }

    [Fact]
    public void Taskbar_is_the_bottom_strip_of_its_monitor()
    {
        Assert.Equal(new RectInt32(0, 1032, 1920, 48), TaskbarLayout.Bounds(new RectInt32(0, 0, 1920, 1080), 96));
    }

    [Fact]
    public void Taskbar_on_a_secondary_monitor_uses_its_coordinates_and_dpi()
    {
        // A 4K monitor at 150% to the left of and above the primary one.
        Assert.Equal(new RectInt32(-3840, -384, 3840, 72), TaskbarLayout.Bounds(new RectInt32(-3840, -2472, 3840, 2160), 144));
    }

    [Fact]
    public void Work_area_is_the_monitor_above_the_taskbar()
    {
        var monitor = new RectInt32(1920, -100, 2560, 1440);
        RectInt32 taskbar = TaskbarLayout.Bounds(monitor, 120);

        Assert.Equal(new RectInt32(1920, -100, 2560, 1380), ShellWorkArea.Compute(monitor, taskbar.Height));
    }

    [Fact]
    public void Taskbars_go_on_every_monitor_or_only_the_primary()
    {
        var primary = new DisplayMonitor(1, new RectInt32(0, 0, 1920, 1080), default, IsPrimary: true, 96);
        var secondary = new DisplayMonitor(2, new RectInt32(1920, 0, 1920, 1080), default, IsPrimary: false, 96);

        Assert.Equal([secondary, primary], TaskbarLayout.MonitorsWithTaskbar([secondary, primary], showOnAllDisplays: true));
        Assert.Equal([primary], TaskbarLayout.MonitorsWithTaskbar([secondary, primary], showOnAllDisplays: false));
    }

    [Fact]
    public void App_bar_keeps_its_height_when_the_shell_moves_its_bottom_edge()
    {
        // Explorer's 48px taskbar is at the bottom; the shell answered ABM_QUERYPOS with a rect ending above it.
        var queried = new User32.RECT { left = 0, top = 1032, right = 1920, bottom = 1032 };

        User32.RECT docked = AppBar.AlignToBottom(queried, 48);

        Assert.Equal(new RectInt32(0, 984, 1920, 48), docked.ToRectInt32());
    }

    [Fact]
    public void Auto_hidden_taskbar_leaves_a_sliver_at_the_bottom_of_the_screen()
    {
        RectInt32 shown = TaskbarLayout.Bounds(new RectInt32(0, 0, 1920, 1080), 96);

        Assert.Equal(new RectInt32(0, 1078, 1920, 48), TaskbarLayout.HiddenBounds(shown));
    }

    [Theory]
    [InlineData(960, 1079, true)]   // the screen's last row
    [InlineData(0, 1078, true)]     // the sliver's top row, at the corner
    [InlineData(960, 1077, false)]  // just above the sliver
    [InlineData(1920, 1079, false)] // on the next monitor
    public void Auto_hidden_taskbar_comes_back_with_the_pointer_on_its_sliver(int x, int y, bool expected)
    {
        RectInt32 shown = TaskbarLayout.Bounds(new RectInt32(0, 0, 1920, 1080), 96);

        Assert.Equal(expected, TaskbarLayout.RevealsAt(new PointInt32(x, y), shown));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)] // borderless full screen
    [InlineData(-8, -8, 1936, 1096, true)] // overhanging the edges
    [InlineData(0, 0, 1920, 1032, false)] // stops at the taskbar
    [InlineData(100, 100, 800, 600, false)]
    [InlineData(1920, 0, 1920, 1080, false)] // full screen on the next monitor
    public void Full_screen_means_covering_the_whole_monitor(int x, int y, int width, int height, bool expected)
    {
        Assert.Equal(expected, TaskbarLayout.IsFullScreen(new RectInt32(x, y, width, height), new RectInt32(0, 0, 1920, 1080)));
    }

    [Theory]
    [InlineData(0, true)]     // the screen's corner
    [InlineData(11, true)]    // the margin left of the button
    [InlineData(30, true)]    // the button itself
    [InlineData(55, false)]   // past it
    public void Left_aligned_start_zone_reaches_the_screen_edge(double x, bool expected)
    {
        Assert.Equal(expected, TaskbarLayout.IsStartZone(x, startLeft: 13, startRight: 53, leftAligned: true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(786, false)]  // further left than the gap
    [InlineData(787, true)]   // the gap left of the button
    [InlineData(839, true)]
    [InlineData(840, false)]  // the next button
    public void Centred_start_zone_takes_only_the_gap_beside_the_button(double x, bool expected)
    {
        Assert.Equal(expected, TaskbarLayout.IsStartZone(x, startLeft: 800, startRight: 840, leftAligned: false));
    }
}
