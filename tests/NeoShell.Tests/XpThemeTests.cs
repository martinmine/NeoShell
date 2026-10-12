using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.StartMenu;
using NeoShell.Taskbar;
using NeoShell.Themes;
using NeoShell.Themes.WindowsXp;

namespace NeoShell.Tests;

public sealed class XpThemeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NeoShell.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static WindowInfo Window(nint handle, string program, string title = "") =>
        new(handle, title, "Class", true, false, false, false, false, 0, 1, null, $@"C:\Apps\{program}.exe");

    [Fact]
    public void Windows_xp_is_a_theme_saved_by_name()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));

        store.Save(new ShellSettings { Theme = ShellTheme.WindowsXP });

        Assert.Equal("Windows XP", ShellThemes.Name(ShellTheme.WindowsXP));
        Assert.Contains("\"Theme\": \"WindowsXP\"", File.ReadAllText(store.Path));
        Assert.Equal(ShellTheme.WindowsXP, store.Load().Theme);
    }

    [Fact]
    public void Buttons_are_160_wide_until_the_band_is_full_then_share_it_as_xp_did()
    {
        Assert.Equal([160, 160, 160, 160, 160, 160], XpTaskbarLayout.ButtonWidths(6, 979));
        // Measured on XP: 7 buttons in 979 pixels were 139 and 140, 8 were 122 and 123, the wider ones last.
        Assert.Equal([139, 140, 140, 140, 140, 140, 140], XpTaskbarLayout.ButtonWidths(7, 979));
        Assert.Equal([122, 122, 122, 122, 122, 123, 123, 123], XpTaskbarLayout.ButtonWidths(8, 979));
        Assert.Empty(XpTaskbarLayout.ButtonWidths(0, 979));
    }

    [Fact]
    public void Each_window_has_its_own_button_beside_its_programs_others_while_they_fit()
    {
        WindowInfo[] windows = [Window(1, "notepad"), Window(2, "cmd"), Window(3, "notepad")];

        IReadOnlyList<TaskButtonModel> buttons = XpTaskbarLayout.Group(windows, 979, groupSimilar: true);

        Assert.Equal([1, 3, 2], buttons.Select(button => (int)button.Windows.Single().Handle));
    }

    [Fact]
    public void A_full_taskbar_groups_the_program_with_the_most_windows_first()
    {
        // XP: cmd and 8 Notepads (9 buttons of 108) grouped Notepad; cmd and 7 Notepads (122 each) didn't.
        List<WindowInfo> windows = [Window(100, "cmd"), .. Enumerable.Range(1, 8).Select(i => Window(i, "notepad"))];

        IReadOnlyList<TaskButtonModel> buttons = XpTaskbarLayout.Group(windows, 979, groupSimilar: true);
        IReadOnlyList<TaskButtonModel> fewer = XpTaskbarLayout.Group(windows[..8], 979, groupSimilar: true);

        Assert.Equal(2, buttons.Count);
        Assert.Equal(8, buttons[1].Windows.Count);
        Assert.Equal(8, fewer.Count);
    }

    [Fact]
    public void Grouping_off_leaves_every_window_its_button()
    {
        List<WindowInfo> windows = [.. Enumerable.Range(1, 12).Select(i => Window(i, "notepad"))];

        Assert.Equal(12, XpTaskbarLayout.Group(windows, 979, groupSimilar: false).Count);
    }

    [Fact]
    public void The_notification_area_fits_its_icons_and_the_clock_as_xp_measured()
    {
        // Four icons and "7:29 AM" (40 pixels) took 147 at XP's right; one icon (collapsed) 93.
        Assert.Equal(147, XpTaskbarLayout.TrayWidth(4, 40));
        Assert.Equal(93, XpTaskbarLayout.TrayWidth(1, 40));
    }

    [Fact]
    public void Quick_launch_shows_three_buttons_and_a_chevron_for_more()
    {
        Assert.Equal(0, XpTaskbarLayout.QuickLaunchWidth(0));
        Assert.Equal(69, XpTaskbarLayout.QuickLaunchWidth(3));
        // XP's tasks began at 198 with Quick Launch's chevron, Quick Launch at 106.
        Assert.Equal(198 - 106, XpTaskbarLayout.QuickLaunchWidth(5));
    }

    [Theory]
    [InlineData("Untitled - Notepad", "Notepad", "Untitled")]
    [InlineData("Notepad", "Notepad", "Notepad")]
    [InlineData("Report.txt - notepad", "Notepad", "Report.txt")]
    [InlineData("Inbox - Outlook", "Notepad", "Inbox - Outlook")]
    public void A_group_menu_leaves_out_the_programs_name(string title, string program, string expected) =>
        Assert.Equal(expected, XpTaskbarLayout.GroupItemTitle(title, program));

    [Fact]
    public void The_system_menu_allows_what_the_windows_state_does()
    {
        var normal = new SystemMenuWindow(IsMinimized: false, IsMaximized: false, HasMinimizeBox: true, HasMaximizeBox: true, IsSizable: true);
        var minimized = normal with { IsMinimized = true };
        var maximized = normal with { IsMaximized = true };
        const uint Restore = 0xF120, Move = 0xF010, Size = 0xF000, Minimize = 0xF020, Maximize = 0xF030, Close = 0xF060;

        Assert.Equal([false, true, true, true, true, true], new[] { Restore, Move, Size, Minimize, Maximize, Close }.Select(c => normal.IsEnabled(c)));
        Assert.Equal([true, false, false, false, true, true], new[] { Restore, Move, Size, Minimize, Maximize, Close }.Select(c => minimized.IsEnabled(c)));
        Assert.Equal([true, false, false, true, false, true], new[] { Restore, Move, Size, Minimize, Maximize, Close }.Select(c => maximized.IsEnabled(c)));
        Assert.Null(normal.IsEnabled(0x1234));
        Assert.Equal(Maximize, normal.DefaultCommand);
        Assert.Equal(Restore, minimized.DefaultCommand);
        Assert.Null((normal with { HasMaximizeBox = false }).DefaultCommand);
        Assert.Equal(SystemMenuGlyph.Close, SystemMenuWindow.GlyphFor(Close));
    }

    [Fact]
    public void Start_menu_folders_merge_the_users_and_everyones()
    {
        string user = Path.Combine(_directory, "user"), common = Path.Combine(_directory, "common");
        Directory.CreateDirectory(Path.Combine(user, "Accessories"));
        Directory.CreateDirectory(Path.Combine(common, "Accessories"));
        Directory.CreateDirectory(Path.Combine(common, "Games"));
        File.WriteAllText(Path.Combine(user, "Accessories", "Notepad.lnk"), "");
        File.WriteAllText(Path.Combine(common, "Accessories", "Paint.lnk"), "");
        File.WriteAllText(Path.Combine(common, "Accessories", "Notepad.lnk"), "");
        File.WriteAllText(Path.Combine(user, "Zebra.url"), "");
        File.WriteAllText(Path.Combine(common, "Apple.lnk"), "");

        IReadOnlyList<StartMenuEntry> entries = StartMenuFolders.Read([user, common]);

        Assert.Equal(["Accessories", "Games", "Apple", "Zebra"], entries.Select(entry => entry.Name));
        Assert.True(entries[0].IsFolder);
        Assert.Equal(["Notepad", "Paint"], entries[0].Children.Select(entry => entry.Name));
        Assert.Equal(Path.Combine(user, "Accessories", "Notepad.lnk"), entries[0].Children[0].Path);
    }

    [Theory]
    [InlineData("Notepad.lnk", "Notepad")]
    [InlineData("MSN.url", "MSN")]
    [InlineData("readme.txt", "readme.txt")]
    public void Start_menu_items_hide_shortcut_extensions(string file, string name) =>
        Assert.Equal(name, StartMenuFolders.DisplayName(file));

    [Fact]
    public void Start_lays_its_columns_out_as_xp_measured()
    {
        XpStartLayout layout = XpStartMenuPainter.Layout(
            [("Internet", "Internet Explorer"), ("E-mail", "Microsoft Outlook")],
            ["MSN", "Windows Media Player", "Windows Messenger", "Tour Windows XP", "Files and Settings Transfer Wizard", "Notepad"],
            [[("a", "My Documents", true), ("b", "My Recent Documents", true), ("c", "My Pictures", true), ("d", "My Music", true), ("e", "My Computer", true)],
             [("f", "Control Panel", false)]]);

        // XP's Start was 478 high; MSN 161 below its top, the lines at 156 and 401, All Programs at 408,
        // Control Panel at 231 under a line at 226, the bottom bar at 437.
        Assert.Equal(478, layout.Height);
        Assert.Equal(161, layout.Items.Single(item => item.Text == "MSN").Top);
        Assert.Equal([156, 401], layout.LeftSeparators);
        Assert.Equal(408, layout.AllProgramsTop);
        Assert.Equal(231, layout.Items.Single(item => item.Text == "Control Panel").Top);
        Assert.Equal([226], layout.RightSeparators);
        Assert.Equal(437, layout.BottomTop);
    }
}
