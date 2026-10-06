using NeoShell.Desktop;
using NeoShell.Interop.Shell;
using NeoShell.Settings;

namespace NeoShell.Tests;

public sealed class DesktopTests
{
    private const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    private static readonly DesktopLocations Locations =
        new(@"C:\Users\ann\Desktop", @"C:\Users\Public\Desktop", @"C:\Users\ann");

    private static readonly Dictionary<string, int> NoChoices = [];

    private static DesktopItem File(string path, string? name = null) =>
        new([], name ?? Path.GetFileName(path), name ?? Path.GetFileName(path), path, path, attributes: 0);

    private static DesktopItem Folder(string path) =>
        new([], Path.GetFileName(path), Path.GetFileName(path), path, path, DesktopFolder.SFGAO_FOLDER);

    private static DesktopItem System(string parsingName, string name) =>
        new([], name, name, parsingName, path: null, DesktopFolder.SFGAO_FOLDER);

    [Theory]
    [InlineData(@"C:\Users\ann\Desktop\notes.txt", true)]
    [InlineData(@"c:\users\ann\desktop\Notes.txt", true)]
    [InlineData(@"C:\Users\Public\Desktop\Microsoft Edge.lnk", true)]
    [InlineData(@"C:\Users\ann\Desktop", false)] // the Desktop folder itself
    [InlineData(@"C:\Users\ann\Documents", false)]
    [InlineData(@"C:\Users\ann\OneDrive", false)]
    [InlineData(@"D:\", false)]
    [InlineData(@"C:\Users\ann\Desktop\Sub\deeper.txt", false)]
    public void Files_show_when_directly_in_a_desktop_folder(string path, bool shown)
    {
        Assert.Equal(shown, DesktopContents.IsShown(File(path), Locations, NoChoices));
    }

    [Fact]
    public void Only_the_recycle_bin_shows_by_default()
    {
        Assert.True(DesktopContents.IsShown(System(RecycleBin, "Recycle Bin"), Locations, NoChoices));
        Assert.False(DesktopContents.IsShown(System(ThisPc, "This PC"), Locations, NoChoices));
        Assert.False(DesktopContents.IsShown(Folder(@"C:\Users\ann"), Locations, NoChoices));
        // Libraries and the like are in the namespace but never on the desktop.
        Assert.False(DesktopContents.IsShown(System("::{031E4825-7B94-4DC3-B131-E946B44C8DD5}", "Libraries"), Locations, NoChoices));
    }

    [Fact]
    public void System_icons_follow_the_users_choices()
    {
        var choices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["{645FF040-5081-101B-9F08-00AA002F954E}"] = 1,
            ["{20d04fe0-3aea-1069-a2d8-08002b30309d}"] = 0,
            ["{59031A47-3F72-44A7-89C5-5595FE6B30EE}"] = 0,
        };

        Assert.False(DesktopContents.IsShown(System(RecycleBin, "Recycle Bin"), Locations, choices));
        Assert.True(DesktopContents.IsShown(System(ThisPc, "This PC"), Locations, choices));
        // The user's files folder has its path as parsing name.
        Assert.True(DesktopContents.IsShown(Folder(@"C:\Users\ann"), Locations, choices));
    }

    [Fact]
    public void Sort_puts_system_icons_first_then_folders_then_files_by_name()
    {
        DesktopEntry[] entries =
        [
            Entry(File(@"C:\Users\ann\Desktop\b10.txt")),
            Entry(File(@"C:\Users\ann\Desktop\b2.txt")),
            Entry(Folder(@"C:\Users\ann\Desktop\Zeta")),
            Entry(System(RecycleBin, "Recycle Bin"), systemIcon: 3),
            Entry(File(@"C:\Users\Public\Desktop\Alpha.lnk")),
            Entry(System(ThisPc, "This PC"), systemIcon: 0),
        ];

        Assert.Equal(
            ["This PC", "Recycle Bin", "Zeta", "Alpha.lnk", "b2.txt", "b10.txt"],
            DesktopContents.Sort(entries, DesktopSortOrder.Name).Select(entry => entry.Item.Name));
    }

    [Fact]
    public void Sort_by_size_type_and_date()
    {
        DateTime day = new(2026, 10, 1);
        DesktopEntry[] entries =
        [
            Entry(File(@"C:\Users\ann\Desktop\a.txt"), size: 300, modified: day),
            Entry(File(@"C:\Users\ann\Desktop\b.docx"), size: 100, modified: day.AddDays(2)),
            Entry(File(@"C:\Users\ann\Desktop\c.txt"), size: 200, modified: day.AddDays(1)),
        ];

        Assert.Equal(["b.docx", "c.txt", "a.txt"], Names(DesktopContents.Sort(entries, DesktopSortOrder.Size)));
        Assert.Equal(["b.docx", "a.txt", "c.txt"], Names(DesktopContents.Sort(entries, DesktopSortOrder.ItemType)));
        Assert.Equal(["b.docx", "c.txt", "a.txt"], Names(DesktopContents.Sort(entries, DesktopSortOrder.DateModified)));
    }

    [Theory]
    [InlineData(32, 32)]
    [InlineData(96, 96)]
    [InlineData(64, 64)]
    [InlineData(null, 48)]
    [InlineData(0, 48)]
    [InlineData(1000, 48)]
    public void Icon_size_falls_back_to_medium(int? stored, int expected)
    {
        Assert.Equal(expected, DesktopViewSettings.ClampIconSize(stored));
    }

    [Fact]
    public void Icons_fill_columns_from_the_top_left()
    {
        Assert.Equal(
            [new GridCell(0, 0), new GridCell(0, 1), new GridCell(0, 2), new GridCell(1, 0), new GridCell(1, 1)],
            DesktopGrid.Arrange([null, null, null, null, null], rows: 3));
    }

    [Fact]
    public void Placed_icons_keep_their_cells_and_the_rest_fill_the_gaps()
    {
        GridCell[] cells = DesktopGrid.Arrange([null, new GridCell(0, 0), null, new GridCell(3, 1)], rows: 3);

        Assert.Equal([new GridCell(0, 1), new GridCell(0, 0), new GridCell(0, 2), new GridCell(3, 1)], cells);
    }

    [Fact]
    public void An_icon_whose_cell_is_taken_or_off_the_grid_goes_to_the_nearest_free_one()
    {
        // The second wants the first's cell; the third a row the screen no longer has.
        GridCell[] cells = DesktopGrid.Arrange([new GridCell(2, 1), new GridCell(2, 1), new GridCell(5, 9)], rows: 4);

        Assert.Equal(new GridCell(2, 1), cells[0]);
        Assert.Equal(1, Math.Abs(cells[1].Column - 2) + Math.Abs(cells[1].Row - 1));
        Assert.Equal(new GridCell(5, 3), cells[2]);
    }

    [Fact]
    public void Moved_icons_go_to_their_target_or_the_nearest_free_cell_on_screen()
    {
        GridCell[] cells = [new(0, 0), new(0, 1), new(0, 2)];

        // Onto a free cell; onto a taken one (beside it); past the screen's edge (kept on it).
        Assert.Equal(new GridCell(4, 2), DesktopGrid.Move(cells, [(0, new GridCell(4, 2))], rows: 3, columns: 6)[0]);
        Assert.Equal(new GridCell(1, 0), DesktopGrid.Move(cells, [(2, new GridCell(0, 0))], rows: 3, columns: 6)[2]);
        Assert.Equal(new GridCell(5, 2), DesktopGrid.Move(cells, [(0, new GridCell(9, 7))], rows: 3, columns: 6)[0]);
    }

    [Fact]
    public void Icons_moved_together_take_each_others_cells()
    {
        // Both move down a row: the first into the second's old cell, which is free as the second moves too; the
        // second finds the third there, which stays put, and takes the closest free cell, the leftmost of those.
        GridCell[] cells = DesktopGrid.Move(
            [new(0, 0), new(0, 1), new(0, 2)], [(0, new GridCell(0, 1)), (1, new GridCell(0, 2))], rows: 4, columns: 2);

        Assert.Equal(new GridCell(0, 1), cells[0]);
        Assert.Equal(new GridCell(0, 3), cells[1]);
        Assert.Equal(new GridCell(0, 2), cells[2]);
    }

    [Fact]
    public void Several_icons_dropped_on_one_cell_gather_around_it()
    {
        GridCell[] cells = DesktopGrid.Move(
            [new(0, 0), new(0, 1), new(0, 2)], [(1, new GridCell(3, 3)), (2, new GridCell(3, 3))], rows: 6, columns: 6);

        Assert.Equal(new GridCell(3, 3), cells[1]);
        Assert.Equal(1, Math.Abs(cells[2].Column - 3) + Math.Abs(cells[2].Row - 3));
    }

    private static DesktopEntry Entry(DesktopItem item, int systemIcon = -1, long size = 0, DateTime modified = default) =>
        new(item, systemIcon, size, modified);

    private static IEnumerable<string> Names(IEnumerable<DesktopEntry> entries) => entries.Select(entry => entry.Item.Name);
}
