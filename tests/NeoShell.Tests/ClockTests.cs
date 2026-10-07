using NeoShell.Taskbar;

namespace NeoShell.Tests;

public class ClockTests
{
    [Fact]
    public void Explorer_defaults_show_the_clock_without_seconds_or_bell()
    {
        ClockSettings settings = ClockSettings.FromValues(null, null, null, []);
        Assert.True(settings.ShowClock);
        Assert.False(settings.ShowSeconds);
        Assert.False(settings.ShowBell);
    }

    [Fact]
    public void Settings_follow_their_registry_values()
    {
        ClockSettings settings = ClockSettings.FromValues(0, 1, 1, []);
        Assert.False(settings.ShowClock);
        Assert.True(settings.ShowSeconds);
        Assert.True(settings.ShowBell);
    }

    [Theory]
    [InlineData("14:05:09", "Latn;", "14∶05∶09")]
    [InlineData("2:05 PM", "Latn;", "2∶05 PM")]
    [InlineData("14:05", "Cyrl;Latn;", "14:05")]
    [InlineData("14:05", "Jpan;", "14:05")]
    public void Colons_become_ratio_signs_only_in_latin_only_locales(string formatted, string scripts, string expected) =>
        Assert.Equal(expected, ClockDisplay.Time(formatted, scripts));

    [Fact]
    public void Tooltip_lists_the_local_time_then_the_additional_clocks()
    {
        string tip = ClockDisplay.ToolTip("Wednesday, 7 October 2026",
            [("Wed", "14:41", "Local time"), ("Wed", "21:41", "Tokyo clock")]);
        Assert.Equal("Wednesday, 7 October 2026\n\nWed 14:41 (Local time)\nWed 21:41 (Tokyo clock)", tip);
    }

    [Fact]
    public void No_bell_unless_it_is_turned_on_or_do_not_disturb_is()
    {
        Assert.Null(ClockDisplay.Bell(showBell: false, doNotDisturb: false, newCount: 3));
        Assert.NotNull(ClockDisplay.Bell(showBell: false, doNotDisturb: true, newCount: 0));
    }

    [Theory]
    [InlineData(false, 0, "", false, "No new notifications")]
    [InlineData(false, 1, "", true, "1 new notification")]
    [InlineData(false, 5, "", true, "5 new notifications")]
    [InlineData(true, 0, "", false, "No new notifications (Do not disturb on)")]
    [InlineData(true, 2, "", false, "2 new notifications (Do not disturb on)")]
    public void Bell_shows_new_notifications_and_do_not_disturb(bool doNotDisturb, int count, string glyph, bool accent, string toolTip) =>
        Assert.Equal(new BellLook(glyph, accent, toolTip), ClockDisplay.Bell(showBell: true, doNotDisturb, count));
}
