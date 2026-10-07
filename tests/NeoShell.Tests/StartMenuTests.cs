using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Settings;
using NeoShell.StartMenu;

namespace NeoShell.Tests;

public sealed class StartMenuTests
{
    [Theory]
    [InlineData("Notepad", "notepad", 0)]
    [InlineData("Notepad", "note", 1)]
    [InlineData("Visual Studio Code", "code", 2)]
    [InlineData("Visual Studio Code", "studio code", 2)]
    [InlineData("Visual Studio Code", "vis cod", 2)]
    [InlineData("Windows PowerShell (x86)", "x86", 2)]
    [InlineData("Notepad", "pad", 3)]
    [InlineData("Notepad", "paint", -1)]
    [InlineData("Notepad", "", -1)]
    [InlineData("Notepad", "   ", -1)]
    [InlineData("Paint", "  PAINT ", 0)]
    public void Score_ranks_exact_then_prefix_then_word_start_then_contains(string name, string query, int expected)
    {
        Assert.Equal(expected, AppSearch.Score(name, query));
    }

    [Fact]
    public void Rank_puts_better_matches_first_and_ties_in_alphabetical_order()
    {
        string[] apps = ["Snipping Tool", "Paint", "Paint 3D", "Microsoft Paint Tool", "Command Prompt", "Unpainted"];

        Assert.Equal(["Paint", "Paint 3D", "Microsoft Paint Tool", "Unpainted"], AppSearch.Rank(apps, a => a, "paint"));
    }

    [Fact]
    public void Packaged_app_is_kept_by_its_app_id()
    {
        var entry = new AppCatalogEntry("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null);

        Assert.Equal(new PinnedApp("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null), StartCatalog.ToPinnedApp(entry));
    }

    [Fact]
    public void Shortcut_with_an_app_id_keeps_its_target_so_windows_without_one_still_match()
    {
        var entry = new AppCatalogEntry("Visual Studio Code", "Microsoft.VisualStudioCode", @"C:\Tools\VS Code\Code.exe");

        Assert.Equal(new PinnedApp("Visual Studio Code", "Microsoft.VisualStudioCode", @"C:\Tools\VS Code\Code.exe"), StartCatalog.ToPinnedApp(entry));
    }

    [Fact]
    public void Path_entry_is_kept_by_its_resolved_path()
    {
        // {1AC14E77-...} is FOLDERID_System.
        var entry = new AppCatalogEntry("Character Map", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", null);

        PinnedApp app = StartCatalog.ToPinnedApp(entry);

        Assert.Null(app.AppUserModelId);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "charmap.exe"), app.Path, ignoreCase: true);
    }

    [Theory]
    [InlineData("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", true)]
    [InlineData("MSEdge", true)]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", false)]
    [InlineData(@"C:\Tools\tool.exe", false)]
    [InlineData("https://nodejs.org/", false)]
    public void App_ids_are_told_apart_from_paths_and_urls(string id, bool expected)
    {
        Assert.Equal(expected, AppCatalog.IsAppUserModelId(id));
    }

    [Fact]
    public void Known_folder_prefix_is_split_off()
    {
        Assert.Equal(
            (new Guid("6D809377-6AF0-444B-8957-A3773F02200E"), @"Git\cmd\git-gui.exe"),
            AppCatalog.SplitKnownFolder(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Git\cmd\git-gui.exe"));
        Assert.Null(AppCatalog.SplitKnownFolder(@"C:\Tools\tool.exe"));
        Assert.Null(AppCatalog.SplitKnownFolder(@"{not-a-guid}\tool.exe"));
    }

    [Theory]
    [InlineData(new[] { "folder" }, StartCatalog.Folders)]
    [InlineData(new[] { "document" }, StartCatalog.Documents)]
    [InlineData(new[] { "picture" }, StartCatalog.Documents)]
    [InlineData(new[] { "Music" }, StartCatalog.Documents)]
    [InlineData(new[] { "program" }, StartCatalog.Other)]
    [InlineData(new string[0], StartCatalog.Other)]
    public void Search_results_are_grouped_by_kind(string[] kinds, string expected)
    {
        Assert.Equal(expected, StartCatalog.GroupFor(new IndexResult("x", @"C:\x", kinds)));
    }

    [Theory]
    [InlineData("Notepad", "N")]
    [InlineData("éclair", "É")]
    [InlineData("7-Zip", "#")]
    [InlineData("", "#")]
    public void All_apps_letter_is_the_first_letter_or_hash(string name, string expected)
    {
        Assert.Equal(expected, StartCatalog.LetterFor(name));
    }

    [Theory]
    [InlineData("  quarterly   report ", "quarterly report")]
    [InlineData("report", "report")]
    [InlineData("\t", "")]
    public void Index_query_whitespace_is_collapsed(string query, string expected)
    {
        Assert.Equal(expected, IndexSearch.Normalize(query));
    }

    [Fact]
    public void Index_kinds_column_is_read_whatever_shape_it_comes_in()
    {
        Assert.Equal(["folder"], IndexSearch.ReadKinds("folder"));
        Assert.Equal(["document", "picture"], IndexSearch.ReadKinds(new[] { "document", "picture" }));
        Assert.Equal(["document"], IndexSearch.ReadKinds(new object[] { "document", 5 }));
        Assert.Empty(IndexSearch.ReadKinds(DBNull.Value));
    }

    [Fact]
    public void Index_query_searches_names_of_files_and_leaves_out_shortcuts()
    {
        Assert.Equal("System.ItemNameDisplay", IndexSearch.ContentProperties);
        Assert.Equal("AND SCOPE='file:' AND System.FileExtension <> '.lnk'", IndexSearch.WhereRestrictions);
        Assert.StartsWith("System.ItemNameDisplay,System.ItemPathDisplay,", IndexSearch.SelectColumns);
    }

    private static readonly PinnedApp s_notepad = new("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
    private static readonly PinnedApp s_code = new("Visual Studio Code", "Microsoft.VisualStudioCode", @"C:\Tools\VS Code\Code.exe");
    private static readonly PinnedApp s_charmap = new("Character Map", Path: Path.Combine(Environment.SystemDirectory, "charmap.exe"));
    private static readonly PinnedApp[] s_catalog = [s_notepad, s_code, s_charmap];

    [Fact]
    public void Explorer_layout_lists_packaged_apps_and_shortcuts_in_order()
    {
        const string json = """
            {"pinnedList":[
              {"packagedAppId":"Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"},
              {"desktopAppLink":"%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\File Explorer.lnk"},
              {"secondaryTile":{}}
            ]}
            """;

        Assert.Equal(
            [
                ("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null),
                (null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\File Explorer.lnk")),
            ],
            StartLayout.Parse(json));
        Assert.Empty(StartLayout.Parse("{}"));
    }

    [Fact]
    public void Explorer_pins_become_catalog_apps_and_removed_apps_are_dropped()
    {
        ExplorerPin[] pins =
        [
            new(null, @"c:\tools\vs code\code.exe"),
            new("Removed_8wekyb3d8bbwe!App", null),
            new("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null),
            new("Microsoft.VisualStudioCode", @"C:\Tools\VS Code\Code.exe"),
        ];

        Assert.Equal([s_code, s_notepad], StartCatalog.FromExplorerPins(s_catalog, pins));
    }

    [Fact]
    public void Taskbar_favorites_give_each_pins_id_list_in_order()
    {
        byte[] first = [0x04, 0x00, 0x00, 0x00]; // an ID list of one empty item: cb 4, then the terminator
        byte[] second = [0x06, 0x00, 0xAA, 0xBB, 0x00, 0x00];
        byte[] favorites = [0x00, .. Entry(first), 0x00, .. Entry(second), 0xFF];

        Assert.Equal([first, second], TaskbarFavorites.Parse(favorites));
    }

    [Fact]
    public void Taskbar_favorites_stop_at_a_truncated_or_empty_value()
    {
        byte[] idList = [0x04, 0x00, 0x00, 0x00];

        Assert.Equal([idList], TaskbarFavorites.Parse([0x00, .. Entry(idList), 0x00, 0x10, 0x00, 0x00, 0x00, 0x01]));
        Assert.Empty(TaskbarFavorites.Parse([0x00]));
        Assert.Empty(TaskbarFavorites.Parse([]));
    }

    private static byte[] Entry(byte[] idList) => [.. BitConverter.GetBytes(idList.Length), .. idList];

    [Fact]
    public void Imported_pins_follow_neoshells_own_and_skip_apps_already_pinned()
    {
        var code = s_code with { DisplayName = "Code" };

        Assert.Equal([s_charmap, s_code, s_notepad], StartCatalog.AddImported([s_charmap, s_code], [code, s_notepad]));
        Assert.Equal([s_notepad], StartCatalog.AddImported([], [s_notepad]));
    }

    [Fact]
    public void UserAssist_names_are_rot13()
    {
        Assert.Equal("Microsoft.Windows.Explorer", UserAssist.Rot13("Zvpebfbsg.Jvaqbjf.Rkcybere"));
        Assert.Equal(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", UserAssist.Rot13(@"{1NP14R77-02R7-4R5Q-O744-2RO1NR5198O7}\puneznc.rkr"));
    }

    [Fact]
    public void UserAssist_value_gives_the_last_start_and_never_started_apps_are_skipped()
    {
        var lastRun = new DateTime(2026, 10, 4, 8, 42, 29, DateTimeKind.Utc);
        byte[] data = new byte[72];
        BitConverter.GetBytes(lastRun.ToFileTimeUtc()).CopyTo(data, 60);

        Assert.Equal(new AppUsage("MSEdge", lastRun), UserAssist.Parse("ZFRqtr", data));
        Assert.Null(UserAssist.Parse("ZFRqtr", new byte[72]));
        Assert.Null(UserAssist.Parse("ZFRqtr", new byte[16]));
    }

    [Fact]
    public void UserAssist_record_of_a_packaged_app_started_by_NeoShell_reads_as_Explorers()
    {
        // As shell32 wrote it for Paint started from NeoShell's Start (2026-10-07): the same layout as Explorer's Start
        // leaves — 8 starts, 24 focus changes, focus time, ten -1.0f, -1, the last run.
        byte[] data = Convert.FromHexString(
            "000000000800000018000000164b0600000080bf000080bf000080bf000080bf000080bf000080bf000080bf000080bf000080bf"
            + "000080bfffffffff6068c96f9156dd0100000000");

        AppUsage? usage = UserAssist.Parse("Zvpebfbsg.Cnvag_8jrxlo3q8oojr!Ncc", data);

        Assert.Equal("Microsoft.Paint_8wekyb3d8bbwe!App", usage?.Id);
        Assert.Equal(new DateTime(2026, 10, 7, 19, 24, 13, 670, DateTimeKind.Utc), usage?.LastRun);
    }

    [Fact]
    public void Recent_apps_are_newest_first_listed_once_and_only_from_the_catalog()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        AppUsage[] usage =
        [
            new("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", now.AddHours(-3)),
            new(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", now.AddHours(-1)),
            new(@"D:\setup.exe", now),
            new("Microsoft.VisualStudioCode", now.AddHours(-2)),
            new(@"C:\Tools\VS Code\Code.exe", now.AddMinutes(-90)),
        ];

        Assert.Equal(
            [(s_charmap, now.AddHours(-1)), (s_code, now.AddMinutes(-90))],
            StartCatalog.Recent(s_catalog, usage, 2));
    }

    [Fact]
    public void Last_run_is_minutes_or_hours_ago_then_the_date()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Local);

        Assert.Equal("Just now", StartCatalog.LastRunText(now.AddSeconds(-20), now));
        Assert.Equal("30m ago", StartCatalog.LastRunText(now.AddMinutes(-30), now));
        Assert.Equal("5h ago", StartCatalog.LastRunText(now.AddHours(-5).AddMinutes(-10), now));
        Assert.Equal(now.AddDays(-2).ToString("M"), StartCatalog.LastRunText(now.AddDays(-2), now));
    }

    // Menus as text: commands by name, separators as "|".
    private static string Show(IEnumerable<AppCommand?> layout) =>
        string.Join(" ", layout.Select(command => command?.ToString() ?? "|"));

    [Fact]
    public void An_app_menu_keeps_Explorers_order_and_parts_its_groups_with_one_separator_each()
    {
        // Edge pinned first: Explorer's menu had exactly this.
        HashSet<AppCommand> edge = [AppCommand.UnpinFromTaskbar, AppCommand.OpenFileLocation, AppCommand.MoveRight, AppCommand.RunAsAdministrator,
            AppCommand.UnpinFromStart, AppCommand.NewFolder];
        Assert.Equal("MoveRight NewFolder UnpinFromStart | RunAsAdministrator OpenFileLocation | UnpinFromTaskbar", Show(StartAppMenu.Layout(edge)));

        // A packaged app in All apps: nothing to move, no file location.
        HashSet<AppCommand> notepad = [AppCommand.Uninstall, AppCommand.AppSettings, AppCommand.PinToTaskbar, AppCommand.RunAsAdministrator, AppCommand.PinToStart];
        Assert.Equal("PinToStart | RunAsAdministrator | PinToTaskbar AppSettings Uninstall", Show(StartAppMenu.Layout(notepad)));

        // File Explorer's own commands make a group of their own.
        HashSet<AppCommand> explorer = [AppCommand.MoveToFront, AppCommand.MoveLeft, AppCommand.UnpinFromStart, AppCommand.Manage, AppCommand.ItemProperties,
            AppCommand.MapNetworkDrive, AppCommand.DisconnectNetworkDrive, AppCommand.UnpinFromTaskbar];
        Assert.Equal("MoveToFront MoveLeft UnpinFromStart | Manage ItemProperties MapNetworkDrive DisconnectNetworkDrive | UnpinFromTaskbar",
            Show(StartAppMenu.Layout(explorer)));
    }

    [Fact]
    public void A_folder_menu_has_only_its_moves_and_an_app_in_a_folder_is_taken_out_before_unpinning()
    {
        Assert.Equal("MoveLeft MoveRight", Show(StartAppMenu.Layout(new HashSet<AppCommand> { AppCommand.MoveRight, AppCommand.MoveLeft })));
        Assert.Equal("RemoveFromFolder UnpinFromStart | PinToTaskbar AppSettings",
            Show(StartAppMenu.Layout(new HashSet<AppCommand> { AppCommand.AppSettings, AppCommand.UnpinFromStart, AppCommand.PinToTaskbar, AppCommand.RemoveFromFolder })));
        Assert.Empty(StartAppMenu.Layout(new HashSet<AppCommand>()));
    }

    [Fact]
    public void Search_results_list_an_apps_commands_in_one_run_in_the_search_boxs_order()
    {
        HashSet<AppCommand> commands = [AppCommand.Uninstall, AppCommand.PinToTaskbar, AppCommand.PinToStart, AppCommand.OpenFileLocation, AppCommand.RunAsAdministrator];

        Assert.Equal(
            [AppCommand.RunAsAdministrator, AppCommand.OpenFileLocation, AppCommand.PinToStart, AppCommand.PinToTaskbar, AppCommand.Uninstall],
            StartAppMenu.SearchLayout(commands));
    }

    [Theory]
    [InlineData("runas", AppCommand.RunAsAdministrator)]
    [InlineData("RunAs", AppCommand.RunAsAdministrator)]
    [InlineData("OpenFileLocation", AppCommand.OpenFileLocation)]
    [InlineData("Uninstall", AppCommand.Uninstall)]
    [InlineData("connectNetworkDrive", AppCommand.MapNetworkDrive)]
    [InlineData("disconnectNetworkDrive", AppCommand.DisconnectNetworkDrive)]
    [InlineData("ItemProperties", AppCommand.ItemProperties)]
    [InlineData("Manage", AppCommand.Manage)]
    public void The_app_folders_verbs_become_Starts_commands(string verb, AppCommand expected)
    {
        Assert.Equal(expected, StartAppMenu.FromVerb(verb));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("OpenNewWindow")]
    [InlineData("link")]
    [InlineData("PinToStartScreen")]
    [InlineData("taskbarunpin")]
    [InlineData(null)]
    public void Verbs_Start_leaves_out_have_no_command(string? verb)
    {
        Assert.Null(StartAppMenu.FromVerb(verb));
    }
}
