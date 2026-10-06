using System.Buffers.Binary;
using NeoShell.Interop.Tray;
using NeoShell.Tray;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class AppBarTests
{
    // The monitor of the VM the expected rectangles were measured on with Explorer as the shell, its taskbar 48 high.
    private static readonly RectInt32 Monitor = Edges(0, 0, 1764, 988);
    private static readonly RectInt32[] Monitors = [Monitor];
    private static readonly RectInt32[] Taskbar = [Edges(0, 940, 1764, 988)];

    private static RectInt32 Edges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    /// <summary>The WM_COPYDATA payload shell32's SHAppBarMessage sends to Shell_TrayWnd (0x40 bytes).</summary>
    private static byte[] Payload(uint message, uint window, uint callback, uint edge, RectInt32 rect, long lParam, long shared, uint processId, byte padding = 0)
    {
        byte[] data = new byte[0x40];
        // A 32-bit caller leaves the padding uninitialized.
        data.AsSpan().Fill(padding);
        Span<byte> span = data;
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x28);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x04..], window);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x08..], callback);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0C..], edge);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x10..], rect.X);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x14..], rect.Y);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x18..], rect.X + rect.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x1C..], rect.Y + rect.Height);
        BinaryPrimitives.WriteInt64LittleEndian(span[0x20..], lParam);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x28..], message);
        BinaryPrimitives.WriteInt64LittleEndian(span[0x30..], shared);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x38..], processId);
        return data;
    }

    [Fact]
    public void A_64_bit_callers_message_is_parsed()
    {
        AppBarMessage message = AppBarMessage.Parse(Payload(3, 0x000A0B0C, 0xC123, 1, Edges(0, 0, 1764, 80), 0, 0x1F4, 4321))!;

        Assert.Equal(AppBarCommand.SetPos, message.Command);
        Assert.Equal((nint)0xA0B0C, message.Window);
        Assert.Equal(0xC123u, message.CallbackMessage);
        Assert.Equal(AppBarEdge.Top, message.Edge);
        Assert.Equal(Edges(0, 0, 1764, 80), message.Rect);
        Assert.Equal((nint)0x1F4, message.SharedMemory);
        Assert.Equal(4321u, message.ProcessId);
    }

    [Fact]
    public void A_32_bit_callers_message_has_the_same_layout_with_sign_extended_values()
    {
        // SysWOW64's shell32 sign-extends lParam and the shared memory handle, and leaves the padding as it was.
        AppBarMessage message = AppBarMessage.Parse(Payload(12, 0x00401A2C, 0xC0DE, 3, Monitor, -1, unchecked((int)0x80000F00), 99, padding: 0xCC))!;

        Assert.Equal(AppBarCommand.SetAutoHideBarEx, message.Command);
        Assert.Equal((nint)0x401A2C, message.Window);
        Assert.Equal(AppBarEdge.Bottom, message.Edge);
        Assert.Equal(-1L, message.LParam);
        Assert.Equal(unchecked((nint)(int)0x80000F00), message.SharedMemory);
        Assert.Equal(99u, message.ProcessId);
    }

    [Fact]
    public void Window_handles_with_the_high_bit_set_are_sign_extended()
    {
        AppBarMessage message = AppBarMessage.Parse(Payload(0, 0xFFFF1234, 0, 0, default, 0, 0, 0))!;

        Assert.Equal(unchecked((nint)(int)0xFFFF1234), message.Window);
    }

    [Fact]
    public void A_short_message_is_not_parsed()
    {
        Assert.Null(AppBarMessage.Parse(new byte[0x30]));
    }

    [Fact]
    public void A_first_bar_stays_clear_of_the_taskbar()
    {
        var bar = new AppBarPlace(1, AppBarEdge.None, default);

        RectInt32 rect = AppBarLayout.QueryPos(bar, AppBarEdge.Left, Edges(0, 0, 100, 988), Monitors, Taskbar, []);

        Assert.Equal(Edges(0, 0, 100, 940), rect);
    }

    [Fact]
    public void A_bar_moving_to_an_edge_goes_inside_the_bars_there_and_may_come_out_crossed()
    {
        var bar = new AppBarPlace(2, AppBarEdge.None, default);
        AppBarPlace[] others = [new(1, AppBarEdge.Left, Edges(0, 0, 100, 940))];

        Assert.Equal(Edges(100, 0, 100, 940), AppBarLayout.QueryPos(bar, AppBarEdge.Left, Edges(0, 0, 100, 988), Monitors, Taskbar, others));
        // Explorer doesn't move a rectangle that hangs off the screen back onto it.
        Assert.Equal(Edges(100, 0, 50, 940), AppBarLayout.QueryPos(bar, AppBarEdge.Left, Edges(-50, 0, 50, 988), Monitors, Taskbar, others));
        Assert.Equal(Edges(1700, 0, 1800, 80), AppBarLayout.QueryPos(bar, AppBarEdge.Top, Edges(1700, 0, 1800, 80), Monitors, Taskbar, others));
        Assert.Equal(Edges(0, 900, 1764, 940), AppBarLayout.QueryPos(bar, AppBarEdge.Bottom, Edges(0, 900, 1764, 988), Monitors, Taskbar, others));
        Assert.Equal(Edges(1700, 0, 1764, 940), AppBarLayout.QueryPos(bar, AppBarEdge.Right, Edges(1700, 0, 1764, 988), Monitors, Taskbar, others));
    }

    [Fact]
    public void Side_bars_stay_clear_of_top_and_bottom_bars_but_not_the_other_way_round()
    {
        var top = new AppBarPlace(2, AppBarEdge.Top, Edges(0, 0, 1764, 80));
        var left = new AppBarPlace(1, AppBarEdge.Left, Edges(0, 0, 100, 940));

        Assert.Equal(Edges(0, 80, 120, 940), AppBarLayout.QueryPos(left, AppBarEdge.Left, Edges(0, 0, 120, 988), Monitors, Taskbar, [top]));
        Assert.Equal(Edges(0, 0, 1764, 80), AppBarLayout.QueryPos(top, AppBarEdge.Top, Edges(0, 0, 1764, 80), Monitors, Taskbar, [left]));
    }

    [Fact]
    public void A_bar_moving_to_another_edge_goes_inside_every_bar_on_it()
    {
        var top = new AppBarPlace(2, AppBarEdge.Top, Edges(0, 0, 1764, 80));
        AppBarPlace[] others = [new(1, AppBarEdge.Left, Edges(0, 80, 120, 940))];

        RectInt32 rect = AppBarLayout.QueryPos(top, AppBarEdge.Left, Edges(0, 0, 100, 988), Monitors, Taskbar, others);

        Assert.Equal(Edges(120, 0, 100, 940), rect);
    }

    [Fact]
    public void A_bar_staying_on_its_edge_keeps_inside_only_the_bars_further_out()
    {
        var bar = new AppBarPlace(2, AppBarEdge.Left, Edges(100, 0, 200, 940));
        AppBarPlace[] others =
        [
            new(1, AppBarEdge.Left, Edges(0, 0, 100, 940)),
            new(3, AppBarEdge.Left, Edges(200, 0, 300, 940)),
        ];

        RectInt32 rect = AppBarLayout.QueryPos(bar, AppBarEdge.Left, Edges(0, 0, 100, 988), Monitors, Taskbar, others);

        Assert.Equal(Edges(100, 0, 100, 940), rect);
    }

    [Fact]
    public void A_bar_on_the_right_goes_left_of_the_sidebar()
    {
        var bar = new AppBarPlace(1, AppBarEdge.None, default);
        AppBarPlace[] sidebar = [new(0, AppBarEdge.Right, Edges(1444, 0, 1764, 988))];

        // Only its inner edge is clipped: the app moves it out of the way itself.
        Assert.Equal(Edges(1664, 0, 1444, 940), AppBarLayout.QueryPos(bar, AppBarEdge.Right, Edges(1664, 0, 1764, 988), Monitors, Taskbar, sidebar));
        Assert.Equal(Edges(0, 0, 1764, 80), AppBarLayout.QueryPos(bar, AppBarEdge.Top, Edges(0, 0, 1764, 80), Monitors, Taskbar, sidebar));
    }

    [Fact]
    public void Bars_and_taskbars_on_another_monitor_take_nothing()
    {
        RectInt32 second = Edges(1764, 0, 3684, 1080);
        var bar = new AppBarPlace(2, AppBarEdge.None, default);
        AppBarPlace[] others = [new(1, AppBarEdge.Left, Edges(1764, 0, 1864, 1032))];

        RectInt32 rect = AppBarLayout.QueryPos(bar, AppBarEdge.Left, Edges(0, 0, 100, 988), [Monitor, second], [Edges(1764, 1032, 3684, 1080)], others);

        Assert.Equal(Edges(0, 0, 100, 988), rect);
    }

    [Fact]
    public void A_rect_on_no_monitor_is_fitted_on_the_primary_one()
    {
        var bar = new AppBarPlace(1, AppBarEdge.None, default);

        RectInt32 rect = AppBarLayout.QueryPos(bar, AppBarEdge.Bottom, Edges(5000, 900, 5100, 1000), Monitors, Taskbar, []);

        Assert.Equal(Edges(5000, 900, 5100, 940), rect);
    }

    [Fact]
    public void The_free_area_is_the_monitor_less_the_placed_bars_on_it()
    {
        AppBarPlace[] bars =
        [
            new(1, AppBarEdge.Left, Edges(0, 80, 120, 940)),
            new(2, AppBarEdge.Top, Edges(0, 0, 1764, 80)),
            // Registered but not placed yet: nothing.
            new(3, AppBarEdge.None, default),
            new(4, AppBarEdge.Right, Edges(1800, 0, 1900, 988)),
        ];

        Assert.Equal(Edges(120, 80, 1764, 988), AppBarLayout.FreeArea(Monitor, [Monitor, Edges(1764, 0, 3684, 1080)], bars));
    }

    [Fact]
    public void A_rect_belongs_to_the_monitor_it_overlaps_most()
    {
        RectInt32[] monitors = [Monitor, Edges(1764, 0, 3684, 1080)];

        Assert.Equal(1, AppBarLayout.MonitorIndex(monitors, Edges(1700, 0, 1900, 100)));
        Assert.Equal(0, AppBarLayout.MonitorIndex(monitors, Edges(1600, 0, 1800, 100)));
        Assert.Null(AppBarLayout.MonitorIndex(monitors, Edges(100, 0, 50, 940)));
        Assert.Null(AppBarLayout.MonitorIndex(monitors, Edges(-200, 0, -100, 100)));
    }
}
