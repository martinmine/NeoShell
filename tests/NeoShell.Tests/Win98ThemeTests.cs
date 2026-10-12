using NeoShell.Settings;
using NeoShell.Themes;
using NeoShell.Themes.Windows98;

namespace NeoShell.Tests;

public sealed class Win98ThemeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NeoShell.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // 98's Start as measured: Windows Update, a line, seven items, a line, Log Off and Shut Down.
    private static readonly (string Key, string Text, bool Submenu)?[] s_start =
    [
        ("update", "Windows Update", false), null,
        ("programs", "&Programs", true), ("favorites", "F&avorites", true), ("documents", "&Documents", true),
        ("settings", "&Settings", true), ("find", "&Find", true), ("help", "&Help", false), ("run", "&Run...", false), null,
        ("logoff", "&Log Off IOblako...", false), ("shutdown", "Sh&ut Down...", false),
    ];

    [Fact]
    public void Windows_98_is_a_theme_saved_by_name()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));

        store.Save(new ShellSettings { Theme = ShellTheme.Windows98 });

        Assert.Equal("Windows 98", ShellThemes.Name(ShellTheme.Windows98));
        Assert.Contains("\"Theme\": \"Windows98\"", File.ReadAllText(store.Path));
        Assert.Equal(ShellTheme.Windows98, store.Load().Theme);
    }

    [Fact]
    public void Task_buttons_are_160_wide_3_apart_until_the_band_is_full()
    {
        Assert.Equal([160, 160, 160], Win98TaskbarLayout.ButtonWidths(3, 3 * 160 + 2 * 3));
        Assert.Equal([100, 100, 101], Win98TaskbarLayout.ButtonWidths(3, 307));
        Assert.Empty(Win98TaskbarLayout.ButtonWidths(0, 500));
    }

    [Fact]
    public void Bands_and_the_tray_sit_where_98_had_them()
    {
        // 98 with its four Quick Launch buttons: the task buttons from x 176; without any, right after the first band's gripper.
        Assert.Equal(176, Win98TaskbarLayout.TaskButtonsLeft(4));
        Assert.Equal(69, Win98TaskbarLayout.TaskButtonsLeft(0));
        // Two icons and "10:49 AM" (46 pixels) made a panel 98 wide.
        Assert.Equal(98, Win98TaskbarLayout.TrayWidth(2, 46));
    }

    [Fact]
    public void Start_lays_its_items_out_as_98_measured()
    {
        // The widest text, "Log Off IOblako...", is 84 pixels in MS Sans Serif.
        Win98StartLayout layout = Win98StartMenuPainter.Layout(s_start, 84);

        Assert.Equal(179, layout.Width);
        Assert.Equal(342, layout.Height);
        Assert.Equal([3, 43, 75, 107, 139, 171, 203, 235, 275, 307], layout.Items.Select(item => item.Top));
        Assert.Equal([35, 267], layout.Separators);
        Assert.Equal(["programs", "favorites", "documents", "settings", "find"], layout.Items.Where(item => item.Submenu).Select(item => item.Key));
    }

    [Fact]
    public void Start_draws_its_frame_banner_and_highlight_in_98s_colours()
    {
        ThemeCanvas canvas = Win98StartMenuPainter.Paint(Win98StartMenuPainter.Layout(s_start, 84), hot: "programs");

        // Frame: light grey then white at the top left, dark grey then black at the bottom right.
        Assert.Equal(0xDFDFDFu, Rgb(canvas, 0, 100));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 1, 100));
        Assert.Equal(0x808080u, Rgb(canvas, 177, 100));
        Assert.Equal(0x000000u, Rgb(canvas, 178, 100));
        Assert.Equal(0x000000u, Rgb(canvas, 100, 341));
        // The banner: navy at the top, its art's white "W" at the bottom.
        Assert.Equal(0x000080u, Rgb(canvas, 10, 10));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 10, 342 - 3 - 105 + 97));
        // Programs selected: navy from x 24 to 172, the separator's two lines under Windows Update.
        Assert.Equal(0x000080u, Rgb(canvas, 24, 50));
        Assert.Equal(0x000080u, Rgb(canvas, 172, 50));
        Assert.Equal(0xC0C0C0u, Rgb(canvas, 173, 50));
        Assert.Equal(0x808080u, Rgb(canvas, 26, 38));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 26, 39));
    }

    [Fact]
    public void The_taskbar_draws_98s_edges_buttons_and_tray()
    {
        var canvas = new ThemeCanvas(1024, Win98TaskbarLayout.Height);
        Win98TaskbarPainter.Paint(canvas, new Win98TaskbarScene(
            1024, StartPressed: false, [(69, Win98ButtonState.Normal)],
            [new(176, 160, "My Computer", Active: false, Pressed: false), new(339, 160, "Untitled - Notepad", Active: true, Pressed: false)],
            TrayX: 924, "10:49 AM", ClockX: 969));

        // The taskbar's top edge, and the Start button's bevel (black takes the corners it shares).
        Assert.Equal(0xDFDFDFu, Rgb(canvas, 500, 0));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 500, 1));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 2, 4));
        Assert.Equal(0xDFDFDFu, Rgb(canvas, 3, 5));
        Assert.Equal(0x000000u, Rgb(canvas, 55, 4));
        Assert.Equal(0x808080u, Rgb(canvas, 54, 24));
        // The active window's button: pushed, a white row, then the checkered face.
        Assert.Equal(0x000000u, Rgb(canvas, 339, 4));
        Assert.Equal(0x808080u, Rgb(canvas, 340, 5));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 345, 6));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 342, 7));
        Assert.Equal(0xC0C0C0u, Rgb(canvas, 343, 7));
        // Bands' etched lines and grippers, and the notification area's sunken panel.
        Assert.Equal(0x808080u, Rgb(canvas, 58, 10));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 59, 10));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 62, 6));
        Assert.Equal(0x808080u, Rgb(canvas, 64, 10));
        Assert.Equal(0x808080u, Rgb(canvas, 920, 10));
        Assert.Equal(0x808080u, Rgb(canvas, 924, 10));
        Assert.Equal(0xFFFFFFu, Rgb(canvas, 1021, 10));
    }

    [Fact]
    public void The_banners_art_is_21_by_105()
    {
        Assert.Equal(105, Win98Art.BannerArt.Length);
        Assert.All(Win98Art.BannerArt, row => Assert.Equal(21, row.Length));
        Assert.All(Win98Art.Flag, row => Assert.Equal(16, row.Length));
    }

    private static uint Rgb(ThemeCanvas canvas, int x, int y) => canvas[x, y] & 0xFFFFFF;
}
