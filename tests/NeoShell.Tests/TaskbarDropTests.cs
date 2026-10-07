using NeoShell.Settings;
using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskbarDropTests
{
    private static readonly Dictionary<string, (string, string?)> Shortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\Links\Tool.lnk"] = (@"C:\Apps\tool.exe", null),
        [@"C:\Links\Tool with args.lnk"] = (@"C:\Apps\tool.exe", "-name x"),
        [@"C:\Links\Notes.lnk"] = (@"C:\Docs\notes.txt", null),
    };

    private static PinnedApp? AppToPin(params string[] files) =>
        TaskbarDrop.AppToPin(files, path => Shortcuts.TryGetValue(path, out var link) ? link : null, _ => "Tool");

    [Fact]
    public void Program_pins_itself_under_its_description()
    {
        Assert.Equal(new PinnedApp("Tool", Path: @"C:\Apps\tool.exe"), AppToPin(@"C:\Apps\tool.exe"));
    }

    [Fact]
    public void Shortcut_without_arguments_pins_its_program_under_its_own_name()
    {
        Assert.Equal(new PinnedApp("Tool", Path: @"C:\Apps\tool.exe"), AppToPin(@"C:\Links\Tool.lnk"));
    }

    [Fact]
    public void Shortcut_with_arguments_pins_its_program_with_them()
    {
        Assert.Equal(
            new PinnedApp("Tool with args", Path: @"C:\Apps\tool.exe", Arguments: "-name x"), AppToPin(@"C:\Links\Tool with args.lnk"));
    }

    [Theory]
    [InlineData(@"C:\Docs\notes.txt")] // a document
    [InlineData(@"C:\Links\Notes.lnk")] // a shortcut to a document
    [InlineData(@"C:\Scripts\run.bat")] // a script
    [InlineData(@"C:\Docs")] // a folder
    [InlineData(@"C:\Links\Missing.lnk")] // a shortcut that can't be read
    public void Anything_but_a_program_pins_nothing(string file)
    {
        Assert.Null(AppToPin(file));
    }

    [Fact]
    public void Several_files_or_none_pin_nothing()
    {
        Assert.Null(AppToPin(@"C:\Apps\tool.exe", @"C:\Apps\other.exe"));
        Assert.Null(AppToPin());
    }

    [Theory]
    [InlineData(-30, 0)] // over Start
    [InlineData(21, 0)]
    [InlineData(23, 1)] // past the first button's middle
    [InlineData(100, 2)]
    [InlineData(500, 3)] // the empty taskbar after the buttons
    public void App_goes_before_the_first_button_whose_middle_is_past_the_pointer(double x, int expected)
    {
        (double, double)[] slots = [(0, 44), (44, 44), (88, 44)];
        Assert.Equal(expected, TaskbarDrop.InsertionIndex(slots, x));
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 2, 44)]
    [InlineData(3, 2, 44)]
    public void Buttons_after_the_gap_make_way(int index, int gap, double expected)
    {
        Assert.Equal(expected, TaskbarDrop.GapOffset(index, gap, 44));
    }

    [Fact]
    public void Dropped_app_is_pinned_where_it_was_dropped()
    {
        var edge = new PinnedApp("Edge", AppUserModelId: "MSEdge");
        var store = new PinnedApp("Store", AppUserModelId: "Store");
        var tool = new PinnedApp("Tool", Path: @"C:\Apps\tool.exe");

        (IReadOnlyList<string> order, IReadOnlyList<PinnedApp> pinned) = TaskbarDrop.Pin(
            [("MSEdge", edge), (@"C:\Running\app.exe", null), ("Store", store)], tool, 2);

        Assert.Equal(["MSEdge", @"C:\Running\app.exe", @"C:\Apps\tool.exe", "Store"], order);
        Assert.Equal([edge, tool, store], pinned);
    }

    [Fact]
    public void Running_app_dropped_on_the_taskbar_is_pinned_with_its_windows_where_it_was_dropped()
    {
        var edge = new PinnedApp("Edge", AppUserModelId: "MSEdge");
        var tool = new PinnedApp("Tool", Path: @"C:\Apps\tool.exe");

        (IReadOnlyList<string> order, IReadOnlyList<PinnedApp> pinned) = TaskbarDrop.Pin(
            [(@"C:\Apps\tool.exe", null), ("MSEdge", edge), (@"C:\Running\app.exe", null)], tool, 3);

        Assert.Equal(["MSEdge", @"C:\Running\app.exe", @"C:\Apps\tool.exe"], order);
        Assert.Equal([edge, tool], pinned);
    }
}
