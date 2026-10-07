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
    [InlineData("éclair", "E")]
    [InlineData("7-Zip", "#")]
    [InlineData("& more", "&")]
    [InlineData("(Beta) tool", "&")]
    [InlineData("Привет", AllApps.OtherScripts)]
    [InlineData("", "&")]
    public void All_apps_letter_is_the_first_Latin_letter_hash_ampersand_or_globe(string name, string expected)
    {
        Assert.Equal(expected, AllApps.LetterFor(name));
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
        Assert.Equal(8, usage?.Runs);
        Assert.Equal(TimeSpan.FromMilliseconds(412438), usage?.FocusTime);
    }

    [Fact]
    public void Tile_ids_are_Explorers()
    {
        Assert.Equal("P~Microsoft.Paint_8wekyb3d8bbwe!App", AllApps.TileId("Microsoft.Paint_8wekyb3d8bbwe!App"));
        Assert.Equal("W~MSEdge", AllApps.TileId("MSEdge"));
        Assert.Equal(@"W~{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\narrator.exe", AllApps.TileId(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\narrator.exe"));
    }

    private static readonly string[] s_folders = [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)"];

    [Fact]
    public void Category_is_the_one_Start_saved_and_without_any_Windows_mapping_by_family_path_or_id()
    {
        var saved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["P~Microsoft.Paint_8wekyb3d8bbwe!App"] = 23,
            ["W~Microsoft.Windows.ControlPanel"] = 0,
        };
        var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["microsoft.paint_8wekyb3d8bbwe"] = 7,
            ["microsoft.windowsnotepad_8wekyb3d8bbwe"] = 13,
            [@"\system32\narrator.exe"] = 1,
            ["msedge"] = 2,
            ["microsoft.windows.controlpanel"] = 9,
        };

        // The web service's answer, saved by Start, wins, even when it's Other; an app Start hasn't categorized is Other.
        Assert.Equal(23, AllApps.CategoryFor("Microsoft.Paint_8wekyb3d8bbwe!App", null, saved, mappings, s_folders));
        Assert.Equal(0, AllApps.CategoryFor("Microsoft.Windows.ControlPanel", null, saved, mappings, s_folders));
        Assert.Equal(AllApps.Other, AllApps.CategoryFor("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null, saved, mappings, s_folders));

        // With nothing saved by Start, Windows' mappings.
        Dictionary<string, int> none = [];
        Assert.Equal(7, AllApps.CategoryFor("Microsoft.Paint_8wekyb3d8bbwe!App", null, none, mappings, s_folders));
        Assert.Equal(13, AllApps.CategoryFor("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null, none, mappings, s_folders));
        Assert.Equal(1, AllApps.CategoryFor(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\narrator.exe", @"C:\WINDOWS\system32\narrator.exe", none, mappings, s_folders));
        Assert.Equal(2, AllApps.CategoryFor("MSEdge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", none, mappings, s_folders));
        Assert.Equal(AllApps.Other, AllApps.CategoryFor("Contoso.App", @"D:\Contoso\app.exe", none, mappings, s_folders));
    }

    private static AllAppsEntry Entry(string name, int category, double usage = 0) =>
        new(new PinnedApp(name, $"Test.{name.Replace(" ", "")}"), $"W~Test.{name}", category, usage);

    private static string Show(IReadOnlyList<(int Category, IReadOnlyList<AllAppsEntry> Apps)> categories) =>
        string.Join(" | ", categories.Select(c => $"{AllApps.CategoryName(c.Category)}: {string.Join(", ", c.Apps.Select(a => a.App.DisplayName))}"));

    [Fact]
    public void Categories_merge_as_Explorers_and_small_ones_go_to_Other()
    {
        AllAppsEntry[] apps =
        [
            // Photo & Video, Multimedia Design, Graphics, Personalization always make Creativity.
            Entry("Photos", 7), Entry("Clipchamp", 7), Entry("Paint", 23),
            // Communication and Security always join Productivity.
            Entry("Edge", 2), Entry("Notepad", 13), Entry("Defender", 5),
            // Games joins Entertainment while it has fewer than three apps.
            Entry("Solitaire", 3), Entry("Xbox", 16), Entry("Media Player", 17),
            // Utilities & Tools has enough apps of its own.
            Entry("Calculator", 9), Entry("Clock", 9), Entry("Store", 9),
            // News & Weather goes into Information & Reading, which with two apps goes into Other.
            Entry("News", 25), Entry("Weather", 25),
            // Two developer tools aren't enough for a card.
            Entry("Terminal", 15), Entry("Git Bash", 15),
            Entry("Claude", 0),
        ];

        Assert.Equal(
            "Creativity: Clipchamp, Paint, Photos | Entertainment: Media Player, Solitaire, Xbox | Other: Claude, Git Bash, News, Terminal, Weather | Productivity: Defender, Edge, Notepad | Utilities & Tools: Calculator, Clock, Store",
            Show(AllApps.Categories(apps)));
    }

    [Fact]
    public void Games_with_three_apps_keep_their_own_card()
    {
        AllAppsEntry[] apps = [Entry("Chess", 3), Entry("Go", 3), Entry("Solitaire", 3), Entry("Xbox", 16), Entry("Groove", 17), Entry("Movies", 16)];

        Assert.Equal("Entertainment: Groove, Movies, Xbox | Games: Chess, Go, Solitaire", Show(AllApps.Categories(apps)));
    }

    [Fact]
    public void Categories_are_ordered_by_their_two_most_used_apps_and_apps_by_use_then_name()
    {
        // As Explorer's Start ordered this machine's (2026-10-07): Other first for Claude, Productivity's Explorer and
        // Notepad, then Utilities & Tools.
        AllAppsEntry[] apps =
        [
            Entry("Claude", 0, 306), Entry("Visual Studio Code", 0, 4.6), Entry("Microsoft News", 0),
            Entry("File Explorer", 13, 75), Entry("Notepad", 13, 43), Entry("Microsoft Edge", 13, 20.4), Entry("Outlook", 13),
            Entry("Settings", 9, 62), Entry("Snipping Tool", 9, 10.1), Entry("Microsoft Store", 9, 4.3), Entry("Calculator", 9, 5.1),
        ];

        Assert.Equal(
            "Other: Claude, Visual Studio Code, Microsoft News | Productivity: File Explorer, Notepad, Microsoft Edge, Outlook | Utilities & Tools: Settings, Snipping Tool, Calculator, Microsoft Store",
            Show(AllApps.Categories(apps)));
    }

    [Fact]
    public void Most_used_are_the_six_most_used_apps_that_were_used()
    {
        AllAppsEntry[] apps =
        [
            Entry("A", 0, 1), Entry("B", 0, 9), Entry("C", 0, 0.2), Entry("D", 0, 5), Entry("E", 0, 7), Entry("F", 0, 3),
            Entry("G", 0, 2), Entry("H", 0, 4),
        ];

        Assert.Equal(["B", "E", "D", "H", "F", "G"], AllApps.MostUsedApps(apps).Select(app => app.App.DisplayName));
        Assert.Empty(AllApps.MostUsedApps([Entry("A", 0, 0.4)]));
    }

    [Fact]
    public void Letters_go_symbols_digits_then_A_to_Z_then_other_scripts()
    {
        AllAppsEntry[] apps = [Entry("Zoom", 0), Entry("7-Zip", 0), Entry("Ägypten", 0), Entry("alpha", 0), Entry("Привет", 0), Entry("&Co", 0)];

        Assert.Equal(
            ["& &Co", "# 7-Zip", "A Ägypten alpha", "Z Zoom", AllApps.OtherScripts + " Привет"],
            AllApps.ByLetter(apps).Select(group => group.Letter + " " + string.Join(" ", group.Apps.Select(app => app.App.DisplayName))));
    }

    [Fact]
    public void Use_is_starts_plus_minutes_in_front_over_every_value_of_the_app()
    {
        AppUsage[] usage =
        [
            new("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", DateTime.UtcNow, 13, TimeSpan.FromMinutes(30)),
            new(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", DateTime.UtcNow, 2, TimeSpan.FromSeconds(30)),
            new("Microsoft.VisualStudioCode", DateTime.UtcNow, 1, TimeSpan.Zero),
            new(@"C:\Tools\VS Code\Code.exe", DateTime.UtcNow, 3, TimeSpan.FromMinutes(1)),
            new(@"D:\setup.exe", DateTime.UtcNow, 1, TimeSpan.Zero),
        ];

        IReadOnlyDictionary<PinnedApp, double> scores = AllApps.UsageScores(s_catalog, usage);

        Assert.Equal(43, scores[s_notepad]);
        Assert.Equal(2.5, scores[s_charmap]);
        Assert.Equal(5, scores[s_code]);
    }

    [Fact]
    public void New_is_what_neither_Explorers_Start_nor_NeoShells_has_seen_opened()
    {
        HashSet<string> seen = new(["P~Claude_pzs8sxrjxfjjc!Claude"], StringComparer.OrdinalIgnoreCase);

        Assert.False(AllApps.IsNew("P~CLAUDE_pzs8sxrjxfjjc!Claude", seen, []));
        Assert.True(AllApps.IsNew("P~Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", seen, []));
        Assert.False(AllApps.IsNew("P~Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", seen, ["P~Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"]));
        // Without Explorer's record, nothing is new.
        Assert.False(AllApps.IsNew("P~Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null, []));
    }

    [Fact]
    public void System_is_Starts_list_else_a_package_or_program_of_Windows()
    {
        HashSet<string> system = new(["windows.immersivecontrolpanel_cw5n1h2txyewy", "Microsoft.WindowsCamera_8wekyb3d8bbwe"], StringComparer.OrdinalIgnoreCase);

        const string windows = @"C:\WINDOWS";

        Assert.True(AllApps.IsSystem("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", null, system, windows));
        Assert.True(AllApps.IsSystem("Microsoft.WindowsStore_8wekyb3d8bbwe!App", null, system, windows));
        Assert.False(AllApps.IsSystem("Microsoft.WindowsCamera_8wekyb3d8bbwe!App", null, system, windows));
        Assert.False(AllApps.IsSystem("Microsoft.Paint_8wekyb3d8bbwe!App", null, system, windows));
        Assert.True(AllApps.IsSystem("Microsoft.Windows.Explorer", @"C:\Windows\explorer.exe", system, windows));
        Assert.False(AllApps.IsSystem("MSEdge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", system, windows));
        Assert.True(AllApps.IsSystem(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\magnify.exe", @"C:\Windows\System32\magnify.exe", system, windows));
        Assert.False(AllApps.IsSystem(@"{6D809377-6AF0-444B-8957-A3773F02200E}\nodejs\node.exe", @"C:\Program Files\nodejs\node.exe", system, windows));
        Assert.False(AllApps.IsSystem("Contoso.Tool", @"C:\WindowsApps\tool.exe", system, windows));
    }

    [Fact]
    public void Start_tile_store_gives_the_tiles_with_a_first_seen_time()
    {
        // Start's roamed tile properties (CloudStore), cut down: Claude seen and pinned, Clipchamp seen, Calculator not.
        static byte[] Key(string text) => [(byte)text.Length, .. System.Text.Encoding.Unicode.GetBytes(text)];
        byte[] data =
        [
            0x02, 0, 0, 0, 0x3C, 0x9F, 0xD1, 0xC9, 0x8E, 0x56, 0xDD, 0x01, 0, 0, 0, 0,
            (byte)'C', (byte)'B', 0x01, 0x00,
            0x0D, 0x12, 0x0A, 0x03,
            .. Key("P~Claude_pzs8sxrjxfjjc!Claude"),
            0x0A, 0xC6, 0x0A, 0x90, 0xBF, 0xA5, 0xE1, 0xAC, 0xEE, 0xD4, 0xEE, 0x01, 0xC2, 0x14, 0x01, 0x00, 0xCA, 0x0A, 0x00, 0x00,
            .. Key("P~Clipchamp.Clipchamp_yxz26nhyzhsrt!App"),
            0x0A, 0xC6, 0x0A, 0x95, 0xD9, 0xC5, 0xCE, 0xEC, 0xD1, 0xD5, 0xEE, 0x01, 0x00, 0xCA, 0x0A, 0x00, 0x00,
            .. Key("P~Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            0x0A, 0xC2, 0x14, 0x01, 0x00, 0xCA, 0x0A, 0x00, 0x00,
            0x00,
        ];

        IReadOnlySet<string>? seen = StartAppData.ParseSeenTiles(data);

        Assert.NotNull(seen);
        Assert.Equal(["P~Claude_pzs8sxrjxfjjc!Claude", "P~Clipchamp.Clipchamp_yxz26nhyzhsrt!App"], seen.Order());
        Assert.Null(StartAppData.ParseSeenTiles([1, 2, 3]));
    }

    [Fact]
    public void Category_mappings_take_an_ids_first_category()
    {
        byte[] json = """{ "2": ["msedge", "chrome"], "13": ["Notepad", "msedge"], "x": ["skip"] }"""u8.ToArray();

        IReadOnlyDictionary<string, int> mappings = StartAppData.ParseMappings(json);

        Assert.Equal(2, mappings["MSEdge"]);
        Assert.Equal(13, mappings["notepad"]);
        Assert.False(mappings.ContainsKey("skip"));
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
