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
    public void Below_1920_the_uneven_pair_is_60_and_40()
    {
        // Explorer's on a 1764 wide work area: the first zone 1058 pixels wide.
        var area = new RectInt32(0, 0, 1764, 940);
        IReadOnlyList<SnapZone> pair = SnapLayouts.For(area, 96)[1];

        Assert.Equal(1058, SnapLayouts.Bounds(pair[0], area).Width);
        Assert.Equal(1.0 / 3, SnapLayouts.For(new RectInt32(0, 0, 1920, 1032), 96)[1][1].Width, 6);
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

    private static readonly RectInt32 DragArea = new(0, 0, 1600, 900); // corners: 112 pixels

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
        Assert.Equal(expected, WindowSnap.AtPointer(new PointInt32(x, y), DragArea, 96));
    }

    [Fact]
    public void Edges_are_the_drag_areas_on_any_monitor()
    {
        // A monitor right of the first and above it, 1920 wide; the area ends at its taskbar.
        var dragArea = new RectInt32(1600, -200, 1920, 1032);

        Assert.Equal(SnapPosition.Left, WindowSnap.AtPointer(new PointInt32(1600, 300), dragArea, 96));
        Assert.Equal(SnapPosition.Maximized, WindowSnap.AtPointer(new PointInt32(2400, -200), dragArea, 96));
        Assert.Equal(SnapPosition.Right, WindowSnap.AtPointer(new PointInt32(3519, 300), dragArea, 96));
        Assert.Equal(SnapPosition.None, WindowSnap.AtPointer(new PointInt32(3199, 300), dragArea, 96)); // a sidebar's edge
    }

    [Theory]
    [InlineData(SnapPosition.None, SnapKey.Left, SnapPosition.Left)]
    [InlineData(SnapPosition.Tall, SnapKey.Left, SnapPosition.Left)]
    [InlineData(SnapPosition.Maximized, SnapKey.Left, SnapPosition.None)]
    [InlineData(SnapPosition.Right, SnapKey.Left, SnapPosition.None)]
    [InlineData(SnapPosition.TopRight, SnapKey.Left, SnapPosition.TopLeft)]
    [InlineData(SnapPosition.Left, SnapKey.Right, SnapPosition.None)]
    [InlineData(SnapPosition.Maximized, SnapKey.Right, SnapPosition.None)]
    [InlineData(SnapPosition.BottomLeft, SnapKey.Right, SnapPosition.BottomRight)]
    [InlineData(SnapPosition.None, SnapKey.Up, SnapPosition.Maximized)]
    [InlineData(SnapPosition.Tall, SnapKey.Up, SnapPosition.Maximized)]
    [InlineData(SnapPosition.Left, SnapKey.Up, SnapPosition.TopLeft)]
    [InlineData(SnapPosition.TopLeft, SnapKey.Up, SnapPosition.Maximized)]
    [InlineData(SnapPosition.BottomRight, SnapKey.Up, SnapPosition.Right)]
    [InlineData(SnapPosition.Maximized, SnapKey.Down, SnapPosition.None)]
    [InlineData(SnapPosition.Tall, SnapKey.Down, SnapPosition.None)]
    [InlineData(SnapPosition.Right, SnapKey.Down, SnapPosition.BottomRight)]
    [InlineData(SnapPosition.TopLeft, SnapKey.Down, SnapPosition.Left)]
    [InlineData(SnapPosition.None, SnapKey.Down, SnapPosition.Minimized)]
    [InlineData(SnapPosition.BottomLeft, SnapKey.Down, SnapPosition.Minimized)]
    [InlineData(SnapPosition.None, SnapKey.ShiftUp, SnapPosition.Tall)]
    [InlineData(SnapPosition.Left, SnapKey.ShiftUp, SnapPosition.Left)]
    [InlineData(SnapPosition.TopLeft, SnapKey.ShiftUp, SnapPosition.Left)]
    [InlineData(SnapPosition.BottomRight, SnapKey.ShiftUp, SnapPosition.Right)]
    [InlineData(SnapPosition.Maximized, SnapKey.ShiftUp, SnapPosition.Maximized)]
    [InlineData(SnapPosition.Tall, SnapKey.ShiftDown, SnapPosition.None)]
    [InlineData(SnapPosition.TopRight, SnapKey.ShiftDown, SnapPosition.None)]
    [InlineData(SnapPosition.Maximized, SnapKey.ShiftDown, SnapPosition.None)]
    [InlineData(SnapPosition.None, SnapKey.ShiftDown, SnapPosition.None)]
    public void Win_arrows_move_between_halves_quarters_and_maximized(SnapPosition from, SnapKey key, SnapPosition to)
    {
        Assert.Equal(new SnapMove(to), WindowSnap.AfterKey(from, key));
    }

    [Theory]
    [InlineData(SnapPosition.Right, SnapKey.Right, SnapPosition.Left, 1)]
    [InlineData(SnapPosition.TopRight, SnapKey.Right, SnapPosition.TopLeft, 1)]
    [InlineData(SnapPosition.BottomRight, SnapKey.Right, SnapPosition.BottomLeft, 1)]
    [InlineData(SnapPosition.Left, SnapKey.Left, SnapPosition.Right, -1)]
    [InlineData(SnapPosition.TopLeft, SnapKey.Left, SnapPosition.TopRight, -1)]
    [InlineData(SnapPosition.BottomLeft, SnapKey.Left, SnapPosition.BottomRight, -1)]
    [InlineData(SnapPosition.Left, SnapKey.ShiftRight, SnapPosition.Left, 1)]
    [InlineData(SnapPosition.BottomRight, SnapKey.ShiftLeft, SnapPosition.BottomRight, -1)]
    [InlineData(SnapPosition.Tall, SnapKey.ShiftRight, SnapPosition.Maximized, 1)]
    public void Win_arrows_go_on_to_the_next_monitor(SnapPosition from, SnapKey key, SnapPosition to, int monitor)
    {
        Assert.Equal(new SnapMove(to, monitor), WindowSnap.AfterKey(from, key));
    }

    [Fact]
    public void Monitors_go_round_from_the_last_to_the_first()
    {
        Assert.Equal(1, WindowSnap.Neighbour(3, 0, 1));
        Assert.Equal(0, WindowSnap.Neighbour(3, 2, 1));
        Assert.Equal(2, WindowSnap.Neighbour(3, 0, -1));
        Assert.Equal(0, WindowSnap.Neighbour(2, 1, -1));
    }

    [Fact]
    public void Stretched_a_window_keeps_its_place_across_and_fills_the_height()
    {
        Assert.Equal(new RectInt32(307, 0, 686, 940), WindowSnap.Tall(new RectInt32(307, 200, 686, 493), new RectInt32(0, 0, 1764, 940)));
    }

    // The VM's monitors as measured: 1764x988 and 1280x800 on its right, each with a 48 epx taskbar.
    private static readonly RectInt32 Primary = new(0, 0, 1764, 988);
    private static readonly RectInt32 PrimaryWork = new(0, 0, 1764, 940);
    private static readonly RectInt32 Second = new(1764, 0, 1280, 800);
    private static readonly WindowBorders Borders96 = new(7, 0, 7, 7);

    [Theory]
    [InlineData(100, 100, 400, 300, 1836, 82)]
    [InlineData(10, 10, 400, 300, 1770, 9)]
    [InlineData(600, 400, 400, 300, 2198, 325)]
    [InlineData(1000, 300, 400, 300, 2489, 244)]
    [InlineData(300, 200, 700, 500, 1981, 163)]
    public void A_window_moved_to_another_monitor_keeps_its_share_of_the_way_across(int x, int y, int width, int height, int toX, int toY)
    {
        // Measured on Explorer (Win+Shift+Right), what's drawn of the window rounded as win32k rounds.
        RectInt32 moved = WindowSnap.OnMonitor(new RectInt32(x, y, width, height), Borders96, Primary, Second,
            new RectInt32(1764, 0, 1280, 752), 96, 96, resizable: true);

        Assert.Equal(new RectInt32(toX, toY, width, height), moved);
    }

    [Fact]
    public void A_window_moved_back_comes_out_where_Explorer_put_it()
    {
        RectInt32 moved = WindowSnap.OnMonitor(new RectInt32(1981, 163, 700, 500), Borders96, Second, Primary, PrimaryWork, 96, 96, true);

        Assert.Equal(new RectInt32(302, 201, 700, 500), moved);
    }

    [Fact]
    public void A_window_moved_against_the_far_edges_is_kept_in_the_work_area()
    {
        RectInt32 work = new(1764, 0, 1280, 752);

        // What's drawn of it ends at the screen's right edge and the taskbar.
        Assert.Equal(new RectInt32(2651, 82, 400, 300),
            WindowSnap.OnMonitor(new RectInt32(1300, 100, 400, 300), Borders96, Primary, Second, work, 96, 96, true));
        Assert.Equal(new RectInt32(2351, 259, 700, 500),
            WindowSnap.OnMonitor(new RectInt32(1064, 440, 700, 500), Borders96, Primary, Second, work, 96, 96, true));
    }

    [Theory]
    [InlineData(10, 10, 400, 300, 1770, 9, 500, 375)]
    [InlineData(100, 100, 400, 300, 1835, 82, 500, 375)]
    [InlineData(600, 500, 400, 300, 2198, 373, 500, 375)]
    [InlineData(1064, 440, 700, 500, 2177, 123, 875, 625)]
    public void A_window_moved_to_a_monitor_at_125_percent_grows_with_it(int x, int y, int width, int height, int toX, int toY, int toWidth, int toHeight)
    {
        // Measured on Explorer with the second monitor at 125% (its taskbar 60 pixels high); borders 8 there.
        RectInt32 moved = WindowSnap.OnMonitor(new RectInt32(x, y, width, height), new WindowBorders(8, 0, 8, 8), Primary, Second,
            new RectInt32(1764, 0, 1280, 740), 96, 120, resizable: true);

        Assert.Equal(new RectInt32(toX, toY, toWidth, toHeight), moved);
    }

    [Fact]
    public void A_monitor_lower_down_or_above_moves_the_window_with_it()
    {
        RectInt32 lower = new(1764, 300, 1280, 800);
        RectInt32 above = new(0, -800, 1280, 800);

        Assert.Equal(new RectInt32(1836, 382, 400, 300),
            WindowSnap.OnMonitor(new RectInt32(100, 100, 400, 300), Borders96, Primary, lower, new RectInt32(1764, 300, 1280, 752), 96, 96, true));
        Assert.Equal(new RectInt32(72, -718, 400, 300),
            WindowSnap.OnMonitor(new RectInt32(100, 100, 400, 300), Borders96, Primary, above, new RectInt32(0, -800, 1280, 752), 96, 96, true));
    }

    [Fact]
    public void A_window_too_big_for_the_other_work_area_is_cut_to_it_if_it_can_be_resized()
    {
        RectInt32 work = new(1764, 0, 1280, 752);
        RectInt32 big = new(0, 0, 1500, 900);

        Assert.Equal(new RectInt32(1757, 0, 1294, 759), WindowSnap.OnMonitor(big, Borders96, Primary, Second, work, 96, 96, resizable: true));
        Assert.Equal(new RectInt32(1757, 0, 1500, 900), WindowSnap.OnMonitor(big, Borders96, Primary, Second, work, 96, 96, resizable: false));
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
        var keys = new SnapKeys(_ => true);

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
        var keys = new SnapKeys(_ => true);

        Assert.False(keys.OnKey(0x26, true, out SnapKey? key)); // an arrow on its own
        keys.OnKey(0x26, false, out _);
        keys.OnKey(0x5C, true, out _);
        keys.OnKey(0xA2, true, out _);                            // Win+Ctrl+Left switches virtual desktops
        Assert.False(keys.OnKey(0x25, true, out key));
        Assert.Null(key);
    }

    [Fact]
    public void Win_Shift_arrows_pass_to_Windows_unless_NeoShell_takes_them()
    {
        // Windows moves a window that isn't snapped to the other monitor itself.
        List<SnapKey> asked = [];
        var keys = new SnapKeys(key =>
        {
            asked.Add(key);
            return key != SnapKey.ShiftRight;
        });

        keys.OnKey(0x5B, true, out _);
        keys.OnKey(0xA1, true, out _);
        Assert.False(keys.OnKey(0x27, true, out SnapKey? key));
        Assert.Null(key);
        Assert.False(keys.OnKey(0x27, false, out _));
        Assert.True(keys.OnKey(0x26, true, out key));
        Assert.Equal(SnapKey.ShiftUp, key);
        Assert.True(keys.OnKey(0x26, false, out _));
        keys.OnKey(0xA1, false, out _);
        Assert.True(keys.OnKey(0x27, true, out key));
        Assert.Equal(SnapKey.Right, key);
        Assert.Equal([SnapKey.ShiftRight, SnapKey.ShiftUp, SnapKey.Right], asked);
    }

    [Fact]
    public void Snap_settings_are_on_unless_turned_off()
    {
        Assert.Equal(new SnapSettings(true, true, true, true, true, true), SnapSettings.FromValues(null, null, null, null, null, null));
        Assert.Equal(new SnapSettings(true, false, true, false, true, false), SnapSettings.FromValues("1", 0, 1, 0, null, 0));
        // "Snap windows" off turns off everything under it.
        Assert.Equal(new SnapSettings(false, false, false, false, false, false), SnapSettings.FromValues("0", 1, 1, 1, 1, 1));
    }

    [Theory]
    [InlineData(62, 450, SnapPosition.Left)]       // within 63 of the side: snaps before reaching the edge
    [InlineData(64, 450, SnapPosition.None)]
    [InlineData(1537, 450, SnapPosition.Right)]
    [InlineData(800, 6, SnapPosition.Maximized)]   // within 7 of the top
    [InlineData(800, 8, SnapPosition.None)]
    [InlineData(40, 30, SnapPosition.TopLeft)]
    public void Near_the_edge_a_window_snaps_without_reaching_it(int x, int y, SnapPosition expected)
    {
        Assert.Equal(expected, WindowSnap.AtPointer(new PointInt32(x, y), DragArea, 96, nearEdge: true));
        if (expected != SnapPosition.None)
            Assert.Equal(SnapPosition.None, WindowSnap.AtPointer(new PointInt32(x, y), DragArea, 96, nearEdge: false));
    }

    [Fact]
    public void Suggestions_come_first_with_the_window_in_its_own_zone()
    {
        var area = new RectInt32(0, 0, 1764, 940);

        IReadOnlyList<SnapChoice> none = SnapLayouts.Choices(area, 96, 0);
        IReadOnlyList<SnapChoice> two = SnapLayouts.Choices(area, 96, 2);

        Assert.Equal(4, none.Count);
        Assert.All(none, choice => Assert.False(choice.IsSuggestion));
        Assert.Equal(6, two.Count);                    // as Explorer's flyout on a screen under 1920 wide
        Assert.Equal([-1, 0], two[0].Suggested);       // halves, the first suggestion beside the window
        Assert.Equal([-1, 0, 1], two[1].Suggested);    // a half and the two suggestions stacked
        Assert.Equal(5, SnapLayouts.Choices(area, 96, 1).Count);
    }

    private static readonly RectInt32 WorkArea = new(0, 0, 1600, 900);
    private static readonly RectInt32 LeftHalf = new(0, 0, 800, 900);
    private static readonly RectInt32 RightHalf = new(800, 0, 800, 900);
    private static readonly RectInt32 TopLeft = new(0, 0, 800, 450);
    private static readonly RectInt32 TopRight = new(800, 0, 800, 450);
    private static readonly RectInt32 BottomLeft = new(0, 450, 800, 450);
    private static readonly RectInt32 BottomRight = new(800, 450, 800, 450);

    [Fact]
    public void A_half_alone_leaves_the_other_half()
    {
        var plan = SnapAssistPlan.EmptyZones(SnapAssistPlan.Layouts(WorkArea, 96), LeftHalf, []);

        Assert.NotNull(plan);
        Assert.Equal([RightHalf], plan.Value.Empty);
        Assert.Equal([LeftHalf], SnapAssistPlan.EmptyZones(SnapAssistPlan.Layouts(WorkArea, 96), RightHalf, [])!.Value.Empty);
    }

    [Fact]
    public void A_quarter_alone_leaves_the_other_three_in_reading_order()
    {
        var plan = SnapAssistPlan.EmptyZones(SnapAssistPlan.Layouts(WorkArea, 96), TopLeft, []);

        Assert.Equal([TopRight, BottomLeft, BottomRight], plan!.Value.Empty);
    }

    [Fact]
    public void A_quarter_beside_a_snapped_half_leaves_the_last_quarter()
    {
        var layouts = SnapAssistPlan.Layouts(WorkArea, 96);

        Assert.Equal([BottomRight], SnapAssistPlan.EmptyZones(layouts, TopRight, [LeftHalf])!.Value.Empty);
        Assert.Equal([BottomLeft], SnapAssistPlan.EmptyZones(layouts, TopLeft, [RightHalf])!.Value.Empty);
    }

    [Fact]
    public void A_half_beside_a_snapped_half_completes_the_layout()
    {
        var plan = SnapAssistPlan.EmptyZones(SnapAssistPlan.Layouts(WorkArea, 96), RightHalf, [LeftHalf]);

        Assert.Empty(plan!.Value.Empty);
        Assert.Equal([LeftHalf, RightHalf], plan.Value.Layout);
    }

    [Fact]
    public void A_zone_of_no_layout_offers_nothing()
    {
        Assert.Null(SnapAssistPlan.EmptyZones(SnapAssistPlan.Layouts(WorkArea, 96), new RectInt32(10, 10, 500, 500), []));
    }

    [Fact]
    public void Assist_cards_are_as_high_as_fits_in_rows_of_the_square_root()
    {
        // Explorer's right half on a 1764 by 940 screen with four windows: two rows, previews 260 high.
        (double preview, IReadOnlyList<Switcher.SwitcherSlot> slots, _) = SnapAssistPlan.Arrange([1.753, 1.314, 1.516, 1.516], 856, 913);

        Assert.Equal(2, slots.Max(s => s.Row) + 1);
        Assert.InRange(preview, 255, 265);

        // Five windows in a quarter: two rows still, the second with three.
        (preview, slots, _) = SnapAssistPlan.Arrange([1.756, 1.753, 1.314, 1.516, 1.516], 856, 900);
        Assert.Equal([0, 0, 1, 1, 1], slots.Select(s => s.Row));
        Assert.InRange(preview, 170, 182);
    }

    [Fact]
    public void Assist_cards_shrink_to_fit_the_panel()
    {
        (double preview, IReadOnlyList<Switcher.SwitcherSlot> slots, double width) = SnapAssistPlan.Arrange([1.6, 1.6, 1.6, 1.6, 1.6], 856, 425);

        int rows = slots.Max(s => s.Row) + 1;
        Assert.True(rows * (preview + SnapAssistPlan.HeaderHeight) + (rows - 1) * SnapAssistPlan.Spacing <= 425 - 2 * SnapAssistPlan.TopPadding);
        Assert.True(width <= 856 - 2 * SnapAssistPlan.SidePadding);
    }

    [Fact]
    public void Windows_snapped_together_form_a_group_until_one_leaves()
    {
        var groups = new SnapGroups();
        int changes = 0;
        groups.Changed += () => changes++;

        groups.Join([1, 2, 3]);
        Assert.Equal([1, 2, 3], groups.Of(2));
        groups.Join([3, 4]);                   // 3 moves to a new group with 4
        Assert.Equal([1, 2], groups.Of(1));
        Assert.Equal([3, 4], groups.Of(4));
        groups.Leave(4);                       // a group of one is no group
        Assert.Null(groups.Of(3));
        Assert.Single(groups.All);
        groups.Join([5]);                      // one window alone makes none
        Assert.Single(groups.All);
        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData(new[] { "A", "B" }, "Group | A and 1 other window")]
    [InlineData(new[] { "A", "B", "C" }, "Group | A and 2 other windows")]
    public void Groups_are_named_after_their_most_recent_window(string[] titles, string expected)
    {
        Assert.Equal(expected, SnapGroups.Title(titles));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(0.1, 0.59)] // more than half the way in the first tenth
    [InlineData(0.5, 0.97)]
    public void Decelerate_is_windows_fast_out_slow_in_curve(double t, double expected)
    {
        Assert.Equal(expected, SnapAssistPlan.Decelerate(t), 2);
    }

    [Fact]
    public void Snap_assist_previews_fly_in_from_their_windows()
    {
        var from = new RectInt32(1100, 450, 550, 400);
        var to = new RectInt32(50, 200, 360, 260);

        // As Snap Assist shows, about 90 % of the way, as recorded on Explorer's (C's preview 162 from the left, landing
        // at 51); on its card once the fly-in is over.
        RectInt32 shown = SnapAssistPlan.FlyIn(from, to, TimeSpan.Zero);
        Assert.InRange(shown.X, 140, 180);
        Assert.Equal(to, SnapAssistPlan.FlyIn(from, to, SnapAssistPlan.FlyInDuration - SnapAssistPlan.FlyInShownAt));
        // Before it shows (never seen) it was on its way from the window.
        Assert.Equal(from, SnapAssistPlan.FlyIn(from, to, -SnapAssistPlan.FlyInShownAt));
    }

    [Fact]
    public void The_maximize_button_is_found_by_hit_testing()
    {
        // A window drawn at (100,50) 640 wide whose button answers over 546..591 by 50..81, as Windows' caption.
        var button = new RectInt32(646, 50, 46, 31);
        bool IsButton(PointInt32 p) => p.X >= button.X && p.X < button.X + button.Width && p.Y >= button.Y && p.Y < button.Y + button.Height;

        Assert.Equal(button, MaximizeButton.Around(new PointInt32(660, 60), IsButton));
        Assert.Equal(button, MaximizeButton.Find(new RectInt32(100, 50, 640, 400), 96, IsButton));
        // Nothing answers: where Windows' own caption puts it.
        Assert.Equal(new RectInt32(648, 50, 46, 30), MaximizeButton.Find(new RectInt32(100, 50, 640, 400), 96, _ => false));
    }
}
