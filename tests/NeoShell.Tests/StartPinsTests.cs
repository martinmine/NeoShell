using NeoShell.Interop.Shell;
using NeoShell.Settings;
using NeoShell.StartMenu;

namespace NeoShell.Tests;

public sealed class StartPinsTests
{
    private static readonly PinnedApp s_edge = new("Edge", AppUserModelId: "MSEdge");
    private static readonly PinnedApp s_notepad = new("Notepad", Path: @"C:\Windows\notepad.exe");
    private static readonly PinnedApp s_paint = new("Paint", AppUserModelId: "Microsoft.Paint_8wekyb3d8bbwe!App");
    private static readonly PinnedApp s_todo = new("To Do", AppUserModelId: "Microsoft.Todos_8wekyb3d8bbwe!App");

    private static StartPin Folder(params PinnedApp[] apps) => new(Folder: new StartFolder("f", "", apps));

    // What a grid shows, as names: apps by name, folders as their apps in brackets.
    private static string Show(IEnumerable<StartPin> pins) =>
        string.Join(" ", pins.Select(pin => pin.Folder is { } folder
            ? "[" + string.Join(" ", folder.Apps.Select(app => app.DisplayName)) + "]"
            : pin.App!.DisplayName));

    [Fact]
    public void An_app_dropped_on_another_makes_a_folder_there_with_the_other_app_first()
    {
        IReadOnlyList<StartPin> pins = [new(s_edge), new(s_notepad), new(s_paint)];

        IReadOnlyList<StartPin> grouped = StartPins.Group(pins, 2, 0);

        Assert.Equal("[Edge Paint] Notepad", Show(grouped));
        Assert.Equal("", grouped[0].Folder!.Name);
        Assert.Equal("Folder", StartPins.DisplayName(grouped[0].Folder!));
        Assert.Equal("[Notepad Edge] Paint", Show(StartPins.Group(pins, 0, 1)));
    }

    [Fact]
    public void An_app_dropped_on_a_folder_goes_last_in_it()
    {
        IReadOnlyList<StartPin> pins = [Folder(s_edge, s_notepad), new(s_paint)];

        IReadOnlyList<StartPin> grouped = StartPins.Group(pins, 1, 0);

        Assert.Equal("[Edge Notepad Paint]", Show(grouped));
        Assert.Equal("f", grouped[0].Folder!.Id);
    }

    [Fact]
    public void New_folders_get_ids_of_their_own()
    {
        IReadOnlyList<StartPin> pins = StartPins.NewFolder(StartPins.NewFolder([new(s_edge), new(s_paint)], 0), 1);

        Assert.Equal("[Edge] [Paint]", Show(pins));
        Assert.NotEqual(pins[0].Folder!.Id, pins[1].Folder!.Id);
        Assert.Equal(1, StartPins.IndexOf(pins, pins[1].Folder!.Id));
        Assert.Equal(-1, StartPins.IndexOf(pins, "gone"));
    }

    [Fact]
    public void Moving_shifts_the_pins_between()
    {
        IReadOnlyList<StartPin> pins = [new(s_edge), Folder(s_notepad), new(s_paint)];

        Assert.Equal("Paint Edge [Notepad]", Show(StartPins.Move(pins, 2, 0)));
        Assert.Equal("[Notepad] Paint Edge", Show(StartPins.Move(pins, 0, 2)));
        Assert.Equal("[Notepad Paint Edge]", Show(StartPins.MoveInFolder([Folder(s_edge, s_notepad, s_paint)], 0, 0, 2)));
    }

    [Fact]
    public void An_app_taken_out_goes_where_it_was_dropped_and_the_folder_stays_while_it_has_an_app()
    {
        IReadOnlyList<StartPin> pins = [new(s_edge), Folder(s_notepad, s_paint), new(s_todo)];

        Assert.Equal("Edge [Paint] To Do Notepad", Show(StartPins.TakeOut(pins, 1, 0, 3)));
        Assert.Equal("Notepad Edge [Paint] To Do", Show(StartPins.TakeOut(pins, 1, 0, 0)));
        Assert.Equal("Edge [Notepad] Paint To Do", Show(StartPins.RemoveFromFolder(pins, 1, 1)));
    }

    [Fact]
    public void A_folder_goes_with_its_last_app()
    {
        IReadOnlyList<StartPin> pins = [new(s_edge), Folder(s_notepad), new(s_todo)];

        Assert.Equal("Edge To Do Notepad", Show(StartPins.TakeOut(pins, 1, 0, 3)));
        Assert.Equal("Notepad Edge To Do", Show(StartPins.TakeOut(pins, 1, 0, 0)));
        Assert.Equal("Edge Notepad To Do", Show(StartPins.RemoveFromFolder(pins, 1, 0)));
    }

    [Fact]
    public void Unpinning_finds_the_app_in_folders_and_drops_emptied_folders()
    {
        StartPin untouched = Folder(s_todo);
        IReadOnlyList<StartPin> pins = [new(s_edge), Folder(s_notepad, s_paint), Folder(s_edge with { DisplayName = "Edge copy" }), untouched];

        IReadOnlyList<StartPin> unpinned = StartPins.Unpin(pins, s_edge);

        Assert.Equal("[Notepad Paint] [To Do]", Show(unpinned));
        Assert.Same(untouched, unpinned[1]);
        Assert.Equal("[Notepad] [To Do]", Show(StartPins.Unpin(unpinned, s_paint)));
    }

    [Fact]
    public void Folders_apps_count_as_pinned()
    {
        IReadOnlyList<StartPin> pins = [new(s_edge), Folder(s_notepad)];

        Assert.True(StartPins.Contains(pins, s_notepad));
        Assert.False(StartPins.Contains(pins, s_paint));
        Assert.Equal([s_edge, s_notepad], StartPins.Apps(pins));
        Assert.Equal("Edge [Notepad] Paint", Show(StartPins.Pin(pins, s_paint)));
    }

    [Fact]
    public void Imported_pins_follow_and_skip_apps_already_pinned_even_in_folders()
    {
        IReadOnlyList<StartPin> pins = [Folder(s_notepad)];

        Assert.Equal("[Notepad] Edge", Show(StartPins.AddImported(pins, [s_notepad, s_edge])));
    }

    [Fact]
    public void Renaming_trims_and_an_empty_name_shows_as_folder()
    {
        IReadOnlyList<StartPin> pins = [Folder(s_notepad)];

        Assert.Equal("Tools", StartPins.Rename(pins, 0, "  Tools ")[0].Folder!.Name);
        Assert.Equal("Folder", StartPins.DisplayName(StartPins.Rename(pins, 0, " ")[0].Folder!));
    }

    [Fact]
    public void Visible_places_are_read_in_any_order_and_unknown_ids_are_skipped()
    {
        byte[] value =
        [
            .. new Guid("52730886-51AA-4243-9F7B-2776584659D4").ToByteArray(), // Settings
            .. new Guid("00000000-0000-0000-0000-000000000001").ToByteArray(),
            .. new Guid("2D34D5CE-FA5A-4543-82F2-22E6EAF7773C").ToByteArray(), // Documents
            .. new byte[5], // truncated
        ];

        Assert.Equal([StartPlace.Documents, StartPlace.Settings], StartPlaces.Parse(value).Order());
        Assert.Empty(StartPlaces.Parse([]));
    }

    [Fact]
    public void Places_show_in_starts_order_and_policies_force_them_on_or_off()
    {
        HashSet<StartPlace> chosen = [StartPlace.Settings, StartPlace.Downloads, StartPlace.Music];

        Assert.Equal([StartPlace.Downloads, StartPlace.Music, StartPlace.Settings], StartPlaces.Visible(chosen, _ => null));
        Assert.Equal(
            [StartPlace.Downloads, StartPlace.Network, StartPlace.Settings],
            StartPlaces.Visible(chosen, place => place switch
            {
                StartPlace.Music => 0,
                StartPlace.Network => 1,
                _ => 65535,
            }));
    }
}
