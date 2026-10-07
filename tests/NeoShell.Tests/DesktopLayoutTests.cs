using NeoShell.Desktop;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class DesktopLayoutTests
{
    private const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    /// <summary>
    /// Explorer's own IconLayouts value, saved with two monitors after the Recycle Bin was dragged to the second, its
    /// names swapped for neutral ones: a layout for the primary monitor alone (23x9 cells) and one for both (23x9 and
    /// 16x7), where the Recycle Bin is on the second monitor at column 3, row 2.
    /// </summary>
    private static readonly byte[] ExplorerLayouts = Convert.FromHexString(
        "00000000000000000000000000000000030001000100010007000000000000002C000000000000003A003A007B00360034003500460046003000340030002D00" +
        "35003000380031002D0031003000310042002D0039004600300038002D003000300041004100300030003200460039003500340045007D003E00200020000000" +
        "16000000000000004D006900630072006F0073006F0066007400200045006400670065002E006C006E006B003E0020007C0000000C0000000000000054006F00" +
        "6F006C002E006500780065003E002000200000000E0000000000000050006C0061007900650072002E006C006E006B003E0020007C0000000D00000000000000" +
        "500068006F0074006F002E006A00700067003E002000200000000D000000000000004E006F007400650073002E007400780074003E002000200000000C000000" +
        "00000000500072006F006A0065006300740073003E005C0020000000020000000000000002000100000000000000000001000000000000000200010000000000" +
        "00000000170000000900000001000000070000000000000000000000000000000000000000000000004001000000000000004040020000000000000080400300" +
        "000000000000A0400400000000400000C0400500000040410000C040060002000100000000000000000002000000000000000200010000000000000000001700" +
        "000009000000010000000600000000000000000000000000004001000000000000004040020000000000000080400300000000000000A0400400000000400000" +
        "C0400500000040410000C0400600020001000000000000000000100000000700000000000000010000000000000000004040000000400000");

    /// <summary>The VM's monitors when the fixture was saved: 1764x988 (taskbar 48) and 1280x800 to its right, both at 100%.</summary>
    private static IReadOnlyList<DesktopWorkspace> TwoMonitors(int secondWidth = 1280) => DesktopLayout.Workspaces(
        [(new RectInt32(1764, 0, secondWidth, 752), 96u, false), (new RectInt32(0, 0, 1764, 940), 96u, true)], DesktopViewSettings.MediumIcons);

    private static IReadOnlyList<DesktopWorkspace> PrimaryOnly(int width = 1764, int height = 940) => DesktopLayout.Workspaces(
        [(new RectInt32(0, 0, width, height), 96u, true)], DesktopViewSettings.MediumIcons);

    [Fact]
    public void Explorers_layouts_are_read()
    {
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;

        Assert.Equal(2, layouts.Desktops.Count);
        Assert.Equal("01:(023x009)", layouts.Desktops[0].Key);
        Assert.Equal("01:(023x009)_00:(016x007)", layouts.Desktops[1].Key);
        LayoutWorkspace second = layouts.Desktops[1].Workspaces[1];
        Assert.Equal(new LayoutIcon(RecycleBin, LayoutIconFlags.Marked, 3, 2), Assert.Single(second.Icons));
        LayoutIcon[] primary = [.. layouts.Desktops[0].Workspaces[0].Icons];
        Assert.Contains(new LayoutIcon("Microsoft Edge.lnk", LayoutIconFlags.Marked | LayoutIconFlags.Common, 0, 2), primary);
        Assert.Contains(new LayoutIcon("Projects", LayoutIconFlags.Marked | LayoutIconFlags.Folder, 12, 6), primary);
    }

    [Fact]
    public void Layouts_are_written_back_as_Explorer_wrote_them()
    {
        Assert.Equal(ExplorerLayouts, IconLayouts.Parse(ExplorerLayouts)!.Serialize());
    }

    [Fact]
    public void Values_in_another_format_are_not_read()
    {
        byte[] other = [.. ExplorerLayouts];
        other[16] = 4; // another dictionary version

        Assert.Null(IconLayouts.Parse(other));
        Assert.Null(IconLayouts.Parse(ExplorerLayouts[..100]));
    }

    [Theory]
    [InlineData("Notes.txt>  ", "Notes.txt", LayoutIconFlags.Marked)]
    [InlineData("Microsoft Edge.lnk> |", "Microsoft Edge.lnk", LayoutIconFlags.Marked | LayoutIconFlags.Common)]
    [InlineData("Shared>\\|", "Shared", LayoutIconFlags.Marked | LayoutIconFlags.Folder | LayoutIconFlags.Common)]
    public void Names_carry_their_flags(string text, string name, LayoutIconFlags flags)
    {
        Assert.Equal((name, flags), IconLayouts.ParseName(text));
        Assert.Equal(text, IconLayouts.FormatName(name, flags));
    }

    [Fact]
    public void Workspaces_go_left_to_right_with_Explorers_cell_sizes()
    {
        IReadOnlyList<DesktopWorkspace> workspaces = TwoMonitors();

        Assert.True(workspaces[0].IsPrimary);
        Assert.Equal(new GridSize(23, 9), workspaces[0].Grid);
        Assert.Equal(new GridSize(16, 7), workspaces[1].Grid);
        Assert.Equal("01:(023x009)_00:(016x007)", DesktopLayout.Key(workspaces));
    }

    [Theory]
    // As LVM_GETITEMSPACING reported on the VM, for the work areas there.
    [InlineData(48, new[] { 1764, 940, 1280, 752 }, 76, 103)]
    [InlineData(48, new[] { 1764, 940 }, 76, 101)]
    [InlineData(48, new[] { 1600, 852 }, 76, 102)]
    [InlineData(48, new[] { 1764, 940, 1024, 720 }, 76, 101)]
    [InlineData(96, new[] { 1764, 940, 1280, 752 }, 110, 149)]
    [InlineData(32, new[] { 1764, 940, 1280, 752 }, 76, 83)]
    public void Spacing_is_stretched_to_the_work_areas_as_Explorers(int iconSize, int[] sizes, int width, int height)
    {
        (int, int)[] workAreas = [.. sizes.Chunk(2).Select(size => (size[0], size[1]))];

        Assert.Equal((width, height), DesktopLayout.Spacing(iconSize, workAreas));
    }

    [Fact]
    public void Cells_scale_with_each_monitors_DPI_rounded_down()
    {
        DesktopWorkspace workspace = Assert.Single(DesktopLayout.Workspaces(
            [(new RectInt32(0, 0, 1280, 752), 120u, true)], DesktopViewSettings.MediumIcons));

        // 78x99 at 96 DPI for its 1024x601 there.
        Assert.Equal((97, 123), (workspace.CellWidth, workspace.CellHeight));
        Assert.Equal(new PointInt32(194, 246), workspace.CellOrigin(new GridCell(2, 2)));
        Assert.Equal(new GridSize(13, 6), workspace.Grid);
    }

    [Fact]
    public void The_layout_saved_for_these_monitors_is_used()
    {
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;

        var twoMonitors = DesktopLayout.SavedPlaces(layouts, TwoMonitors());
        var oneMonitor = DesktopLayout.SavedPlaces(layouts, PrimaryOnly());

        Assert.Equal(new IconPlace(1, new GridCell(3, 2)), twoMonitors[(RecycleBin, LayoutIconFlags.Marked)]);
        Assert.Equal(new IconPlace(0, new GridCell(0, 0)), oneMonitor[(RecycleBin, LayoutIconFlags.Marked)]);
        Assert.Equal(new IconPlace(0, new GridCell(0, 3)), oneMonitor[("TOOL.EXE", LayoutIconFlags.Marked)]);
    }

    [Fact]
    public void A_primary_monitor_of_another_size_shares_the_layout_as_in_Explorer()
    {
        // NeoShell's widget sidebar narrows the primary monitor to 19 columns: Explorer links such a desktop to the
        // one only the primary's grid tells apart, so both shells show the same places...
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;
        IReadOnlyList<DesktopWorkspace> narrowed = DesktopLayout.Workspaces(
            [(new RectInt32(0, 0, 1444, 940), 96u, true), (new RectInt32(1764, 0, 1280, 752), 96u, false)], DesktopViewSettings.MediumIcons);

        Assert.Equal(new IconPlace(1, new GridCell(3, 2)), DesktopLayout.SavedPlaces(layouts, narrowed)[(RecycleBin, LayoutIconFlags.Marked)]);

        // ...and the places saved for it replace that other desktop's, which would otherwise bring old places back.
        IconLayouts saved = DesktopLayout.WithPlaces(layouts, narrowed, [(RecycleBin, LayoutIconFlags.Marked, new IconPlace(0, new GridCell(5, 5)))]);
        Assert.Equal(["01:(023x009)", "01:(019x009)_00:(016x007)"], saved.Desktops.Select(desktop => desktop.Key));
        Assert.Equal(new IconPlace(0, new GridCell(5, 5)), DesktopLayout.SavedPlaces(saved, TwoMonitors())[(RecycleBin, LayoutIconFlags.Marked)]);
    }

    [Fact]
    public void A_new_arrangement_takes_the_saved_layout_whose_icons_fit_best()
    {
        // As Explorer did on the VM: the second monitor at 1024 pixels (13 columns) is an arrangement it hadn't seen.
        // The primary-only layout has the very grid of the primary monitor; the two-monitor one only one its icons
        // fit on for the second, so the first wins and the Recycle Bin goes back to the primary monitor.
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;
        IconLayouts moved = DesktopLayout.WithPlaces(layouts, TwoMonitors(),
            [(RecycleBin, LayoutIconFlags.Marked, new IconPlace(1, new GridCell(14, 6)))]);

        var places = DesktopLayout.SavedPlaces(moved, TwoMonitors(secondWidth: 1024));

        Assert.Equal(new IconPlace(0, new GridCell(0, 0)), places[(RecycleBin, LayoutIconFlags.Marked)]);
    }

    [Fact]
    public void A_new_resolution_keeps_the_layout_of_as_many_monitors()
    {
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;

        var places = DesktopLayout.SavedPlaces(layouts, PrimaryOnly(1600, 852));

        Assert.Equal(new IconPlace(0, new GridCell(0, 0)), places[(RecycleBin, LayoutIconFlags.Marked)]);
        Assert.Equal(new IconPlace(0, new GridCell(12, 6)), places[("Projects", LayoutIconFlags.Marked | LayoutIconFlags.Folder)]);
    }

    [Fact]
    public void Icons_of_a_monitor_without_a_counterpart_are_not_placed()
    {
        // Only the two-monitor layout has icons left for a primary of another size and no second monitor...
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;
        layouts = layouts with { Desktops = [layouts.Desktops[1]] };

        var places = DesktopLayout.SavedPlaces(layouts, PrimaryOnly(1600, 852));

        // ...and the Recycle Bin, on the monitor that's gone, joins the new icons.
        Assert.False(places.ContainsKey((RecycleBin, LayoutIconFlags.Marked)));
        Assert.Equal(new IconPlace(0, new GridCell(0, 2)), places[("Microsoft Edge.lnk", LayoutIconFlags.Marked | LayoutIconFlags.Common)]);
    }

    [Fact]
    public void Places_now_replace_the_saved_layout_for_these_monitors_and_keep_the_others()
    {
        IconLayouts layouts = IconLayouts.Parse(ExplorerLayouts)!;

        IconLayouts saved = DesktopLayout.WithPlaces(layouts, TwoMonitors(),
        [
            (RecycleBin, LayoutIconFlags.Marked, new IconPlace(1, new GridCell(15, 6))),
            ("Notes.txt", LayoutIconFlags.Marked, new IconPlace(0, new GridCell(4, 4))),
        ]);
        IconLayouts read = IconLayouts.Parse(saved.Serialize())!;

        Assert.Equal(2, read.Desktops.Count);
        Assert.Equal(layouts.Desktops[0].Workspaces[0].Icons, read.Desktops[0].Workspaces[0].Icons);
        LayoutDesktop both = read.Find("01:(023x009)_00:(016x007)")!;
        Assert.Equal(new LayoutIcon("Notes.txt", LayoutIconFlags.Marked, 4, 4), Assert.Single(both.Workspaces[0].Icons));
        Assert.Equal(new LayoutIcon(RecycleBin, LayoutIconFlags.Marked, 15, 6), Assert.Single(both.Workspaces[1].Icons));
    }

    [Fact]
    public void New_icons_fill_the_primary_monitor_first_then_the_others()
    {
        // The primary is the second workspace here; 2 cells each.
        IconPlace[] places = DesktopGrid.Arrange([null, null, null, null, null], [new GridSize(1, 2), new GridSize(1, 2)], primary: 1);

        Assert.Equal(
            [
                new IconPlace(1, new GridCell(0, 0)), new IconPlace(1, new GridCell(0, 1)),
                new IconPlace(0, new GridCell(0, 0)), new IconPlace(0, new GridCell(0, 1)),
                new IconPlace(1, new GridCell(1, 0)),
            ],
            places);
    }

    [Fact]
    public void Icons_keep_their_monitor_and_cell_and_a_gone_monitors_join_the_new_ones()
    {
        IconPlace[] places = DesktopGrid.Arrange(
            [new IconPlace(1, new GridCell(2, 1)), new IconPlace(2, new GridCell(0, 0)), null],
            [new GridSize(4, 3), new GridSize(4, 3)],
            primary: 0);

        Assert.Equal(new IconPlace(1, new GridCell(2, 1)), places[0]);
        Assert.Equal(new IconPlace(0, new GridCell(0, 0)), places[1]);
        Assert.Equal(new IconPlace(0, new GridCell(0, 1)), places[2]);
    }

    [Fact]
    public void Packing_keeps_each_icon_on_its_monitor()
    {
        IconPlace[] places = DesktopGrid.Pack([1, 0, 1, null, 5], [new GridSize(3, 2), new GridSize(3, 2)], primary: 0);

        Assert.Equal(
            [
                new IconPlace(1, new GridCell(0, 0)), new IconPlace(0, new GridCell(0, 0)), new IconPlace(1, new GridCell(0, 1)),
                new IconPlace(0, new GridCell(0, 1)), new IconPlace(0, new GridCell(1, 0)),
            ],
            places);
    }

    [Fact]
    public void Icons_dragged_to_another_monitor_stay_on_its_grid()
    {
        IconPlace[] places = [new(0, new GridCell(0, 0)), new(1, new GridCell(0, 0))];

        IconPlace[] moved = DesktopGrid.Move(places, [(0, new IconPlace(1, new GridCell(0, 0))), (1, new IconPlace(1, new GridCell(9, 9)))],
            [new GridSize(5, 5), new GridSize(3, 2)]);

        Assert.Equal(new IconPlace(1, new GridCell(0, 0)), moved[0]);
        Assert.Equal(new IconPlace(1, new GridCell(2, 1)), moved[1]);
    }
}
