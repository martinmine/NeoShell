using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;
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

        Assert.Equal(new RectInt32(1920, -100, 2560, 1380), TaskbarLayout.WorkArea(monitor, taskbar));
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
}
