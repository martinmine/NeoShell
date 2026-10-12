using System.Xml.Linq;
using NeoShell.Frames;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Tests;

/// <summary>XP Luna's renderer and layout against the numbers measured in XP (docs/design/window-frames-overlay.md).</summary>
public sealed class LunaFrameTests
{
    private const int Middle = 150; // a column of a 300 px caption clear of its shaded ends

    private static readonly (LunaButton, LunaButtonState)[] s_minMaxClose =
        [(LunaButton.Minimize, LunaButtonState.Normal), (LunaButton.Maximize, LunaButtonState.Normal), (LunaButton.Close, LunaButtonState.Normal)];

    private static LunaWindow Window(int width = 300, int? captionHeight = null, LunaKind kind = LunaKind.Sizable, bool active = true,
        bool maximized = false, string title = "", uint[]? icon = null, (LunaButton, LunaButtonState)[]? buttons = null) =>
        new(width, captionHeight ?? LunaFrame.CaptionHeight(kind, maximized), kind, active, maximized, title, icon, buttons ?? []);

    private static uint At(uint[] pixels, int width, int x, int y) => pixels[y * width + x];

    [Theory]
    [InlineData(true, 0, 0xFF0058EBu)]
    [InlineData(true, 1, 0xFF3D95FFu)]
    [InlineData(true, 10, 0xFF0055E5u)]
    [InlineData(true, 22, 0xFF026AFEu)]
    [InlineData(true, 29, 0xFF0143CFu)]
    [InlineData(false, 0, 0xFF688DE0u)]
    [InlineData(false, 10, 0xFF7A96DFu)]
    [InlineData(false, 29, 0xFF7A93DFu)]
    public void Sizable_caption_rows_are_xps(bool active, int row, uint expected)
    {
        uint[] caption = LunaFrame.RenderCaption(Window(active: active));

        Assert.Equal(expected, At(caption, 300, Middle, row));
    }

    [Fact]
    public void A_dialogs_caption_is_a_sizable_one_without_row_11()
    {
        uint[] dialog = LunaFrame.RenderCaption(Window(kind: LunaKind.Dialog));

        Assert.Equal(29 * 300, dialog.Length);
        Assert.Equal(0xFF0055E5u, At(dialog, 300, Middle, 11)); // sizable row 12
        Assert.Equal(0xFF0143CFu, At(dialog, 300, Middle, 28)); // sizable row 29
    }

    [Fact]
    public void A_taller_title_bar_repeats_a_flat_row_below_the_title_and_buttons()
    {
        // Windows 11's title bars are 31 rows to XP's 30: rows 0-23 stay XP's, behind the title and buttons.
        uint[] caption = LunaFrame.RenderCaption(Window(captionHeight: 31));

        Assert.Equal(0xFF0164F9u, At(caption, 300, Middle, 21));
        Assert.Equal(0xFF026AFEu, At(caption, 300, Middle, 23));
        Assert.Equal(0xFF026AFEu, At(caption, 300, Middle, 24));
        Assert.Equal(0xFF0065FDu, At(caption, 300, Middle, 27)); // XP's row 26
        Assert.Equal(0xFF0143CFu, At(caption, 300, Middle, 30));
    }

    [Fact]
    public void A_maximized_caption_fits_windows_23_rows()
    {
        uint[] caption = LunaFrame.RenderCaption(Window(maximized: true, captionHeight: 23));

        Assert.Equal(0xFF3E96FFu, At(caption, 300, Middle, 0));
        Assert.Equal(0xFF0055E3u, At(caption, 300, Middle, 6));
        Assert.Equal(0xFF0055E6u, At(caption, 300, Middle, 7)); // XP's row 10: rows 7-9 left out
        Assert.Equal(0xFF0043CFu, At(caption, 300, Middle, 22));
        // Square corners: XP put a maximized window's frame off screen.
        Assert.Equal(0xFF000000u, At(caption, 300, 0, 0) & 0xFF000000);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 0)]
    public void Top_corners_are_rounded(int row, int transparent)
    {
        uint[] caption = LunaFrame.RenderCaption(Window());

        for (int x = 0; x < 8; x++)
        {
            Assert.Equal(x < transparent, At(caption, 300, x, row) == 0);
            Assert.Equal(x < transparent, At(caption, 300, 299 - x, row) == 0);
        }
    }

    [Fact]
    public void Buttons_sit_where_xp_put_them()
    {
        // Sizable: 21 px, 23 apart, at y 6, the rightmost ending 6 px inside the right edge.
        Assert.Equal(
            [new RectInt32(327, 6, 21, 21), new RectInt32(350, 6, 21, 21), new RectInt32(373, 6, 21, 21)],
            LunaFrame.ButtonRects(Window(400, buttons: s_minMaxClose)));
        // Dialog frame: at y 5, ending 5 px inside.
        Assert.Equal(
            [new RectInt32(251, 5, 21, 21), new RectInt32(274, 5, 21, 21)],
            LunaFrame.ButtonRects(Window(kind: LunaKind.Dialog, buttons: [(LunaButton.Help, LunaButtonState.Normal), (LunaButton.Close, LunaButtonState.Normal)])));
        // Maximized: at y 2, ending 2 px from the screen's edge.
        Assert.Equal(new RectInt32(1741, 2, 21, 21), LunaFrame.ButtonRects(Window(1764, maximized: true, buttons: s_minMaxClose))[2]);
        // A tool window's small close button.
        Assert.Equal([new RectInt32(182, 5, 13, 13)], LunaFrame.ButtonRects(Window(200, kind: LunaKind.Tool, buttons: [(LunaButton.Close, LunaButtonState.Normal)])));
    }

    [Theory]
    [InlineData("Normal", true, 0xFF3A76F5u)]
    [InlineData("Hot", true, 0xFF4A94FFu)]
    [InlineData("Pressed", true, 0xFF0050B5u)]
    [InlineData("Normal", false, 0xFF597FE9u)]
    [InlineData("Disabled", true, 0xFF1F5DEAu)]
    public void Blue_button_faces_have_xps_centre(string buttonState, bool active, uint expected)
    {
        var state = Enum.Parse<LunaButtonState>(buttonState);
        uint[] caption = LunaFrame.RenderCaption(Window(400, active: active,
            buttons: [(LunaButton.Minimize, state), (LunaButton.Maximize, LunaButtonState.Normal), (LunaButton.Close, LunaButtonState.Normal)]));

        Assert.Equal(expected, At(caption, 400, 327 + 10, 6 + 10));
        // The white rim (half see-through on a disabled button, on an inactive window's).
        if (active && state != LunaButtonState.Disabled)
            Assert.Equal(0xFFFFFFFFu, At(caption, 400, 327, 6 + 10));
    }

    [Fact]
    public void Titles_are_drawn_white_or_pale_left_of_the_buttons()
    {
        string title = new('W', 100);
        uint[] bare = LunaFrame.RenderCaption(Window(400, buttons: s_minMaxClose));
        uint[] active = LunaFrame.RenderCaption(Window(400, title: title, buttons: s_minMaxClose));
        uint[] inactive = LunaFrame.RenderCaption(Window(400, active: false, title: title, buttons: s_minMaxClose));

        Assert.Contains(0xFFFFFFFFu, Region(active, 7, 324));
        Assert.Contains(0xFFD8E4F8u, Region(inactive, 7, 324));
        // Cut off with an ellipsis 3 px before the first button.
        Assert.Equal(Region(bare, 325, 399), Region(active, 325, 399));

        static IEnumerable<uint> Region(uint[] pixels, int left, int right) =>
            Enumerable.Range(0, 30).SelectMany(y => Enumerable.Range(left, right - left + 1).Select(x => pixels[y * 400 + x]));
    }

    [Fact]
    public void The_icon_goes_left_of_the_title()
    {
        uint[] icon = [.. Enumerable.Repeat(0xFFFF0000u, 256)];
        uint[] caption = LunaFrame.RenderCaption(Window(icon: icon));

        Assert.Equal(0xFFFF0000u, At(caption, 300, 6, 8));
        Assert.Equal(0xFFFF0000u, At(caption, 300, 21, 23));
        Assert.NotEqual(0xFFFF0000u, At(caption, 300, 22, 23));
    }

    [Fact]
    public void Borders_are_xps()
    {
        (uint[] left, uint[] right, uint[] bottom) = LunaFrame.RenderBorders(Window(), sideHeight: 10);

        Assert.Equal([0xFF0019CFu, 0xFF0831D9u, 0xFF166AEEu, 0xFF0855DDu], left[..4]);
        Assert.Equal([0xFF0048F1u, 0xFF003DDCu, 0xFF001EA0u, 0xFF00138Cu], right[36..]);
        Assert.Equal(0xFF0048F1u, bottom[Middle]);
        Assert.Equal(0xFF00138Cu, bottom[3 * 300 + Middle]);
        Assert.Equal(0xFF0019CFu, bottom[0]); // the mitred corner

        (uint[] dialogLeft, _, uint[] dialogBottom) = LunaFrame.RenderBorders(Window(kind: LunaKind.Dialog, active: false), 10);
        Assert.Equal([0xFF7480DCu, 0xFF758CDDu, 0xFF758CDCu], dialogLeft[..3]);
        Assert.Equal(0xFF4F53BCu, dialogBottom[2 * 300 + Middle]);
    }

    [Fact]
    public void Icons_are_brought_to_16_pixels()
    {
        // 32x32 in 2x2 blocks: each block becomes one pixel.
        byte[] big = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int i = (y * 32 + x) * 4;
                (big[i], big[i + 1], big[i + 2], big[i + 3]) = ((byte)(x / 2), (byte)(y / 2), 0, 255);
            }
        }
        uint[] icon = LunaFrame.Icon16(32, 32, big);

        Assert.Equal(0xFF000000u, icon[0]);
        Assert.Equal(0xFF00050Au, icon[5 * 16 + 10]); // green = row 5, blue = column 10

        byte[] small = [.. Enumerable.Range(0, 8 * 8).SelectMany(i => new byte[] { (byte)i, 0, 0, 255 })];
        Assert.Equal(0xFF000009u, LunaFrame.Icon16(8, 8, small)[2 * 16 + 2]); // nearest: (1, 1)
    }

    private static WindowCaption Caption(bool maximized = false, bool resizable = true, int titleBarBottom = 131,
        CaptionButtonState minimize = CaptionButtonState.Enabled, CaptionButtonState maximize = CaptionButtonState.Enabled,
        CaptionButtonState help = CaptionButtonState.Hidden, CaptionButtonState close = CaptionButtonState.Enabled,
        bool hasTitleBar = true) =>
        maximized
            ? new(new RectInt32(-8, -8, 1780, 956), new RectInt32(0, 0, 1764, 940), new RectInt32(0, 23, 1764, 917), 23, hasTitleBar, 1626, minimize, maximize, help, close,
                true, resizable, true)
            // A standard window at 100 %: the client 1 px inside the visible frame, a 31 px title bar.
            : new(new RectInt32(93, 100, 814, 607), new RectInt32(100, 100, 800, 600), new RectInt32(101, 131, 798, 568), titleBarBottom, hasTitleBar, 762,
                minimize, maximize, help, close, false, resizable, true);

    [Fact]
    public void Layout_puts_xps_borders_around_the_client_area()
    {
        LunaLayout layout = LunaLayout.From(Caption(), buttonCount: 3)!;

        Assert.Equal(LunaKind.Sizable, layout.Kind);
        Assert.Equal(new RectInt32(97, 100, 806, 31), layout.Caption);
        Assert.Equal(new RectInt32(97, 131, 4, 568), layout.Left);
        Assert.Equal(new RectInt32(899, 131, 4, 568), layout.Right);
        Assert.Equal(new RectInt32(97, 699, 806, 4), layout.Bottom);
        // From Windows' own caption buttons to the right end, so the pointer never reaches them.
        Assert.Equal(new RectInt32(762, 100, 141, 31), layout.Buttons);

        LunaLayout dialog = LunaLayout.From(Caption(resizable: false), 1)!;
        Assert.Equal(LunaKind.Dialog, dialog.Kind);
        Assert.Equal(new RectInt32(98, 100, 804, 31), dialog.Caption);
    }

    [Fact]
    public void Layout_of_a_maximized_window_is_its_caption_only()
    {
        LunaLayout layout = LunaLayout.From(Caption(maximized: true), 3)!;

        Assert.Equal(new RectInt32(0, 0, 1764, 23), layout.Caption);
        Assert.Equal(default, layout.Left);
        Assert.Equal(default, layout.Bottom);
    }

    [Fact]
    public void A_moved_window_moves_every_part()
    {
        LunaLayout layout = LunaLayout.From(Caption(), 3)!;
        LunaLayout moved = layout.Offset(-50, 20);

        Assert.Equal(new RectInt32(47, 120, 806, 31), moved.Caption);
        Assert.Equal(new RectInt32(849, 151, 4, 568), moved.Right);
        Assert.Equal(new RectInt32(47, 719, 806, 4), moved.Bottom);
        Assert.Equal(new RectInt32(712, 120, 141, 31), moved.Buttons);
    }

    [Fact]
    public void Windows_without_a_standard_title_bar_get_no_layout()
    {
        // The app draws its own title bar over the caption (Chromium's way), or hides it.
        Assert.Null(LunaLayout.From(Caption(titleBarBottom: 105), 3));
        Assert.Null(LunaLayout.From(Caption(hasTitleBar: false), 3));
    }

    [Fact]
    public void Buttons_follow_the_windows_own()
    {
        Assert.Equal(s_minMaxClose, LunaLayout.ButtonsFor(Caption(), null, null));
        Assert.Equal(LunaButton.Restore, LunaLayout.ButtonsFor(Caption(maximized: true), null, null)[1].Button);
        // Calculator: no maximize box, so a greyed one.
        Assert.Equal(LunaButtonState.Disabled, LunaLayout.ButtonsFor(Caption(maximize: CaptionButtonState.Hidden), null, null)[1].State);
        // Run: help and close.
        Assert.Equal(
            [(LunaButton.Help, LunaButtonState.Normal), (LunaButton.Close, LunaButtonState.Normal)],
            LunaLayout.ButtonsFor(Caption(minimize: CaptionButtonState.Hidden, maximize: CaptionButtonState.Hidden, help: CaptionButtonState.Enabled), null, null));
        // A message box: close only.
        Assert.Equal(
            [(LunaButton.Close, LunaButtonState.Normal)],
            LunaLayout.ButtonsFor(Caption(minimize: CaptionButtonState.Hidden, maximize: CaptionButtonState.Hidden), null, null));
    }

    [Fact]
    public void Buttons_show_hover_and_press()
    {
        Assert.Equal(LunaButtonState.Hot, LunaLayout.ButtonsFor(Caption(), LunaButton.Close, null)[2].State);
        Assert.Equal(LunaButtonState.Pressed, LunaLayout.ButtonsFor(Caption(), LunaButton.Close, LunaButton.Close)[2].State);
        // Held, but the pointer moved off it: released there, it does nothing.
        Assert.Equal(LunaButtonState.Normal, LunaLayout.ButtonsFor(Caption(), null, LunaButton.Close)[2].State);
        Assert.Equal(LunaButtonState.Normal, LunaLayout.ButtonsFor(Caption(), LunaButton.Minimize, LunaButton.Close)[0].State);
    }

    [Fact]
    public void The_program_reads_the_styles_neoshell_saves()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NeoShell.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            new SettingsStore(path).Save(new ShellSettings
            {
                WindowFramesEnabled = true,
                WindowFrameStyle = FramePresets.XpLuna,
                WindowFrameStyles = [new FrameStyle { Name = "Mine", Overlay = FrameOverlay.Luna }],
                WindowFrameRules = [new FrameRule("regedit.exe"), new FrameRule("calc.exe", Style: "Mine"), new FrameRule("mmc.exe", Style: "Mica")],
            });
            HostSettings settings = HostSettings.Load(path)!;

            Assert.True(settings.DrawsLuna("notepad.exe", "Notepad"));
            Assert.True(settings.DrawsLuna("calc.exe", "CalcFrame"));
            Assert.False(settings.DrawsLuna("regedit.exe", "RegEdit_RegEdit")); // left alone
            Assert.False(settings.DrawsLuna("mmc.exe", "MMCMainFrame")); // a DWM style: NeoShell's
            Assert.False((settings with { WindowFramesEnabled = false }).DrawsLuna("notepad.exe", "Notepad"));

            File.WriteAllText(path, "{ not json");
            Assert.False(HostSettings.Load(path)!.DrawsLuna("notepad.exe", "Notepad"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        Assert.False(HostSettings.Load(path)!.WindowFramesEnabled);
    }

    [Fact]
    public void The_logon_task_runs_the_program_elevated_at_sign_in()
    {
        XDocument task = XDocument.Parse(LogonTask.Xml(@"C:\A & B\NeoShell.Frames.exe", @"VM\mart"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal(@"VM\mart", task.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("HighestAvailable", task.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("\"C:\\A & B\\NeoShell.Frames.exe\"", task.Descendants(ns + "Command").Single().Value);
        Assert.Equal("PT0S", task.Descendants(ns + "ExecutionTimeLimit").Single().Value);
    }
}
