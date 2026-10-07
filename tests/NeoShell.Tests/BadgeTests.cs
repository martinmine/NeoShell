using NeoShell.Interop.Notifications;
using NeoShell.Taskbar;

namespace NeoShell.Tests;

public class BadgeTests
{
    [Theory]
    [InlineData(1, 5u, 0, 5u, BadgeGlyph.None)]
    [InlineData(2, 0u, 2, 0u, BadgeGlyph.Alert)]
    [InlineData(2, 0u, 12, 0u, BadgeGlyph.Attention)]
    public void Store_values_become_badges(int kind, uint number, int glyph, uint expectedNumber, BadgeGlyph expectedGlyph) =>
        Assert.Equal(new AppBadge(expectedNumber, expectedGlyph), AppBadge.From(kind, number, glyph));

    [Theory]
    [InlineData(0, 0u, 0)] // cleared
    [InlineData(1, 0u, 0)] // value="0" and value="none"
    [InlineData(2, 0u, 0)]
    [InlineData(2, 0u, 13)] // a glyph this build doesn't know
    public void Nothing_shows_for_no_badge(int kind, uint number, int glyph) =>
        Assert.Null(AppBadge.From(kind, number, glyph));

    [Theory]
    [InlineData(1u, "1")]
    [InlineData(99u, "99")]
    [InlineData(100u, "99+")]
    [InlineData(1000u, "99+")]
    public void Counts_above_99_show_99_plus(uint number, string text) =>
        Assert.Equal(text, BadgeLook.Text(new AppBadge(number, BadgeGlyph.None)));

    [Fact]
    public void Glyphs_are_Segoe_Fluent_Icons_characters_and_presence_is_a_plain_dot()
    {
        Assert.Equal("", BadgeLook.Text(new AppBadge(0, BadgeGlyph.Activity)));
        Assert.Equal("", BadgeLook.Text(new AppBadge(0, BadgeGlyph.Playing)));
        Assert.Equal("", BadgeLook.Text(new AppBadge(0, BadgeGlyph.Busy)));
    }

    [Fact]
    public void Warnings_and_presence_have_their_own_colours()
    {
        Assert.Equal(Windows.UI.Color.FromArgb(255, 0xD7, 0x3B, 0x02), BadgeLook.Background(BadgeGlyph.Error));
        Assert.Equal(Windows.UI.Color.FromArgb(255, 255, 255, 255), BadgeLook.Foreground(BadgeGlyph.Error));
        Assert.Equal(Windows.UI.Color.FromArgb(255, 0x00, 0x81, 0x17), BadgeLook.Background(BadgeGlyph.Available));
        Assert.Null(BadgeLook.Foreground(BadgeGlyph.Available));
        Assert.Null(BadgeLook.Background(BadgeGlyph.NewMessage));
        Assert.Null(BadgeLook.Background(BadgeGlyph.None));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Badges_are_shown_unless_turned_off(object? value, bool shown) => Assert.Equal(shown, BadgeLook.AreShown(value));

    [Fact]
    public void Screen_readers_hear_Explorers_words()
    {
        Assert.Equal("Status 1 item", BadgeLook.HelpText(new AppBadge(1, BadgeGlyph.None)));
        Assert.Equal("Status 12 items", BadgeLook.HelpText(new AppBadge(12, BadgeGlyph.None)));
        Assert.Equal("Status New message", BadgeLook.HelpText(new AppBadge(0, BadgeGlyph.NewMessage)));
        Assert.Equal("Status Away", BadgeLook.HelpText(new AppBadge(0, BadgeGlyph.Away)));
    }
}
