using NeoShell.Frames;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Tests;

public sealed class WindowFrameTests
{
    private const uint Accent = 0xFF0078D4;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF_0000;
    private const uint WS_POPUP = 0x8000_0000;

    private static WindowFrame.Target Window(uint style = WS_OVERLAPPEDWINDOW, uint exStyle = 0, bool visible = true,
        bool cloaked = false, int processId = 100) =>
        new(processId, "notepad.exe", "Notepad", "Untitled - Notepad", style, exStyle, visible, cloaked);

    [Fact]
    public void Rule_for_process_and_class_wins_over_process_alone_and_global()
    {
        FrameRule[] rules =
        [
            new("notepad.exe", Style: "Mica"),
            new("NOTEPAD.EXE", "#32770", "Luna (XP-like)"),
        ];

        Assert.Equal("Luna (XP-like)", FrameRules.StyleFor(rules, "Acrylic", "notepad.exe", "#32770"));
        Assert.Equal("Mica", FrameRules.StyleFor(rules, "Acrylic", "Notepad.exe", "Notepad"));
        Assert.Equal("Acrylic", FrameRules.StyleFor(rules, "Acrylic", "regedit.exe", "RegEdit_RegEdit"));
        Assert.Equal("Acrylic", FrameRules.StyleFor(rules, "Acrylic", null, "Notepad"));
    }

    [Fact]
    public void First_of_equally_specific_rules_wins_and_leave_alone_gives_null()
    {
        FrameRule[] rules = [new("app.exe"), new("app.exe", Style: "Mica"), new("other.exe", "", "Dark")];

        Assert.Null(FrameRules.StyleFor(rules, "Acrylic", "app.exe", "Main"));
        // An empty class is no class.
        Assert.Equal("Dark", FrameRules.StyleFor(rules, "Acrylic", "other.exe", "Main"));
    }

    [Fact]
    public void Standard_windows_are_eligible()
    {
        Assert.True(FrameRules.IsEligible(Window(), ownProcessId: 1));
        // A tool window or popup with a title bar is styled too.
        Assert.True(FrameRules.IsEligible(Window(WS_POPUP | 0x00C0_0000, exStyle: 0x80), 1));
    }

    [Theory]
    [InlineData(WS_OVERLAPPEDWINDOW, 0u, false, false)] // hidden
    [InlineData(WS_OVERLAPPEDWINDOW, 0u, true, true)] // cloaked
    [InlineData(WS_POPUP, 0u, true, false)] // no title bar
    [InlineData(WS_POPUP | 0x0080_0000, 0u, true, false)] // a border but no caption
    [InlineData(WS_OVERLAPPEDWINDOW, 0x0800_0000u, true, false)] // WS_EX_NOACTIVATE
    [InlineData(WS_OVERLAPPEDWINDOW, 0x20u, true, false)] // WS_EX_TRANSPARENT
    [InlineData(WS_OVERLAPPEDWINDOW | 0x4000_0000, 0u, true, false)] // a child
    public void Other_windows_are_not(uint style, uint exStyle, bool visible, bool cloaked)
    {
        Assert.False(FrameRules.IsEligible(Window(style, exStyle, visible, cloaked), ownProcessId: 1));
    }

    [Fact]
    public void NeoShells_own_windows_are_not()
    {
        Assert.False(FrameRules.IsEligible(Window(processId: 42), ownProcessId: 42));
    }

    [Fact]
    public void Unset_style_sets_nothing()
    {
        FrameAttributes attributes = FrameColors.ToAttributes(new FrameStyle { Name = "x" }, Accent);

        Assert.Equal(new FrameAttributes(), attributes);
        Assert.Equal(FrameParts.None, attributes.Parts);
    }

    [Fact]
    public void Style_maps_to_attributes()
    {
        var style = new FrameStyle
        {
            Name = "x",
            Backdrop = FrameBackdrop.MicaAlt,
            Theme = FrameTheme.Light,
            CaptionColor = "#102030",
            TextColor = "#FFFFFF",
            BorderColor = FrameColors.None,
            Corners = FrameCorners.SmallRound,
            BasicFrame = true,
        };

        FrameAttributes attributes = FrameColors.ToAttributes(style, Accent);

        Assert.Equal(SystemBackdrop.MicaAlt, attributes.Backdrop);
        Assert.False(attributes.DarkMode);
        Assert.Equal(0xFF102030u, attributes.CaptionColor);
        Assert.Equal(0xFFFFFFFFu, attributes.TextColor);
        Assert.Equal(0u, attributes.BorderColor);
        Assert.Equal(CornerPreference.SmallRound, attributes.Corners);
        Assert.True(attributes.BasicFrame);
        Assert.Equal((FrameParts)0x7F, attributes.Parts);
    }

    [Theory]
    [InlineData(FrameBackdrop.None, SystemBackdrop.None)]
    [InlineData(FrameBackdrop.Mica, SystemBackdrop.Mica)]
    [InlineData(FrameBackdrop.Acrylic, SystemBackdrop.Acrylic)]
    public void Backdrops_map_to_dwms(FrameBackdrop backdrop, SystemBackdrop expected)
    {
        Assert.Equal(expected, FrameColors.ToAttributes(new FrameStyle { Backdrop = backdrop }, Accent).Backdrop);
    }

    [Fact]
    public void Accent_preset_takes_the_accent_and_contrasting_text()
    {
        FrameStyle accent = FramePresets.Find("Accent", [])!;

        FrameAttributes onBlue = FrameColors.ToAttributes(accent, 0xFF0078D4);
        FrameAttributes onYellow = FrameColors.ToAttributes(accent, 0x00FFD700);

        Assert.Equal(0xFF0078D4u, onBlue.CaptionColor);
        Assert.Equal(0xFF0078D4u, onBlue.BorderColor);
        Assert.Equal(0xFFFFFFFFu, onBlue.TextColor);
        // The accent comes opaque whatever its alpha.
        Assert.Equal(0xFFFFD700u, onYellow.CaptionColor);
        Assert.Equal(0xFF000000u, onYellow.TextColor);
    }

    [Fact]
    public void Contrast_text_without_a_caption_colour_is_left_alone()
    {
        Assert.Null(FrameColors.ToAttributes(new FrameStyle { TextColor = FrameColors.Contrast }, Accent).TextColor);
    }

    [Theory]
    [InlineData("#0054E3", 0xFF0054E3u)]
    [InlineData("#ffffff", 0xFFFFFFFFu)]
    [InlineData("0054E3", null)]
    [InlineData("#0054E", null)]
    [InlineData("#GG0000", null)]
    [InlineData(null, null)]
    public void Colours_parse(string? text, uint? expected)
    {
        Assert.Equal(expected, FrameColors.Parse(text));
    }

    [Fact]
    public void Colours_format_and_convert_to_colorref()
    {
        Assert.Equal("#0054E3", FrameColors.Format(0xFF0054E3));
        Assert.Equal(0x00E35400u, WindowFrame.ToColorRef(0xFF0054E3));
        // Transparent is no colour at all (DWMWA_COLOR_NONE).
        Assert.Equal(0xFFFFFFFEu, WindowFrame.ToColorRef(0x00000000));
    }

    [Fact]
    public void Parts_left_unset_are_reset_and_parts_set_again_are_not()
    {
        var before = new FrameAttributes { Backdrop = SystemBackdrop.Mica, DarkMode = true, CaptionColor = 0xFF0054E3 };
        var after = new FrameAttributes { DarkMode = false, Corners = CornerPreference.Square };

        Assert.Equal(FrameParts.Backdrop | FrameParts.CaptionColor, FrameColors.PartsToReset(before, after));
        Assert.Equal(FrameParts.None, FrameColors.PartsToReset(null, after));
        Assert.Equal(before.Parts, FrameColors.PartsToReset(before, new FrameAttributes()));
    }

    [Fact]
    public void Presets_are_as_the_spec_lists_them()
    {
        Assert.Equal(
            ["Windows default", "Mica", "Mica Alt", "Acrylic", "Dark", "Light", "Accent", "Glass (Vista-like)", "Luna (XP-like)", "Windows 7 Basic"],
            FramePresets.All.Select(style => style.Name));
        FrameAttributes luna = FrameColors.ToAttributes(FramePresets.Find("Luna (XP-like)", [])!, Accent);
        Assert.Equal(0xFF0054E3u, luna.CaptionColor);
        Assert.Equal(0xFFFFFFFFu, luna.TextColor);
        Assert.Equal(0xFF0831D9u, luna.BorderColor);
        FrameAttributes glass = FrameColors.ToAttributes(FramePresets.Find("Glass (Vista-like)", [])!, Accent);
        Assert.Equal(new FrameAttributes { Backdrop = SystemBackdrop.Acrylic, DarkMode = true, BorderColor = Accent }, glass);
        Assert.Equal(FrameParts.BasicFrame, FrameColors.ToAttributes(FramePresets.Find("Windows 7 Basic", [])!, Accent).Parts);
    }

    [Fact]
    public void Presets_win_over_user_styles_and_copies_get_free_names()
    {
        FrameStyle[] user = [new() { Name = "Mica", Theme = FrameTheme.Dark }, new() { Name = "Luna (XP-like) copy" }];

        Assert.Equal(FrameBackdrop.Mica, FramePresets.Find("Mica", user)!.Backdrop);
        Assert.Same(user[1], FramePresets.Find("Luna (XP-like) copy", user));
        Assert.Null(FramePresets.Find("Missing", user));
        Assert.Equal("Luna (XP-like) copy 2", FramePresets.CopyName("Luna (XP-like)", user));
        Assert.Equal("Dark copy", FramePresets.CopyName("Dark", user));
    }
}
