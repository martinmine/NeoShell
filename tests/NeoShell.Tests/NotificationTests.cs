using System.Globalization;
using System.Xml.Linq;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Notifications;
using NeoShell.Notifications;

namespace NeoShell.Tests;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset s_morning = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    private static ToastInfo Toast(uint id, string app, int minutes) =>
        new(id, app, app + " app", s_morning.AddMinutes(minutes), $"Title {id}", "");

    [Fact]
    public void Groups_are_by_app_with_the_newest_app_first_and_newest_notification_first()
    {
        IReadOnlyList<NotificationGroup> groups = NotificationDisplay.Group(
        [
            Toast(1, "Mail", 0),
            Toast(2, "Chat", 5),
            Toast(3, "Mail", 10),
            Toast(4, "chat", 2), // AppUserModelIDs aren't case sensitive
        ]);

        Assert.Equal(["Mail", "Chat"], groups.Select(g => g.AppId));
        Assert.Equal([3u, 1u], groups[0].Toasts.Select(t => t.Id));
        Assert.Equal([2u, 4u], groups[1].Toasts.Select(t => t.Id));
    }

    [Fact]
    public void Todays_notifications_show_the_time_older_ones_the_date_too()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var today = new DateTime(2026, 10, 4, 18, 0, 0);
        DateTimeOffset earlier = new DateTime(2026, 10, 4, 9, 5, 0, DateTimeKind.Local);
        DateTimeOffset yesterday = new DateTime(2026, 10, 3, 21, 30, 0, DateTimeKind.Local);

        Assert.Equal("09:05", NotificationDisplay.TimeText(earlier, today, culture));
        Assert.Equal("03/10/2026 21:30", NotificationDisplay.TimeText(yesterday, today, culture));
    }

    [Theory]
    [InlineData(1, "+1 notification")]
    [InlineData(7, "+7 notifications")]
    public void Collapsed_groups_count_what_they_hide(int hidden, string expected)
    {
        Assert.Equal(expected, NotificationDisplay.MoreText(hidden));
    }

    [Theory]
    [InlineData("en-GB", "Sunday, 4 October")]
    [InlineData("en-US", "Sunday, October 4")]
    [InlineData("nb-NO", "søndag 4. oktober")]
    [InlineData("de-DE", "Sonntag, 4. Oktober")]
    public void Calendar_heading_is_the_long_date_without_the_year(string culture, string expected)
    {
        Assert.Equal(expected, NotificationDisplay.DayHeading(new DateTime(2026, 10, 4), CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("dddd, d MMMM yyyy", "dddd, d MMMM")]
    [InlineData("dddd, MMMM d, yyyy", "dddd, MMMM d")]
    [InlineData("yyyy'年'M'月'd'日'dddd", "M'月'd'日'dddd")]
    [InlineData("yyyy. MMMM d., dddd", "MMMM d., dddd")]
    [InlineData("dddd d MMMM", "dddd d MMMM")]
    public void Year_comes_out_of_date_patterns_with_its_separator(string pattern, string expected)
    {
        Assert.Equal(expected, NotificationDisplay.WithoutYear(pattern));
    }

    [Theory]
    [InlineData(30, 45, 25)]
    [InlineData(5, 10, 5)]
    [InlineData(25, 30, 20)]
    [InlineData(60, 75, 45)]
    [InlineData(240, 240, 225)]
    public void Focus_length_steps_by_five_minutes_to_half_an_hour_then_by_fifteen(int minutes, int more, int less)
    {
        Assert.Equal(more, NotificationDisplay.MoreFocus(minutes));
        Assert.Equal(less, NotificationDisplay.LessFocus(minutes));
    }

    [Fact]
    public void A_focus_session_hides_badges_and_flashing_as_its_settings_say_then_puts_back_what_was_there()
    {
        var all = new FocusSettings(ClockTimer: true, HideBadges: true, HideFlashing: true, DoNotDisturb: true);
        Assert.Equal(new Dictionary<string, int> { ["TaskbarBadges"] = 0, ["TaskbarFlashing"] = 0 }, FocusChanges.During(all));
        // Badges were turned off by the user, flashing never set (on): Windows writes back 0 and 1.
        var before = new Dictionary<string, object?> { ["TaskbarBadges"] = 0 };
        Assert.Equal(new Dictionary<string, int> { ["TaskbarBadges"] = 0, ["TaskbarFlashing"] = 1 },
            FocusChanges.After(all, name => before.GetValueOrDefault(name)));

        var badgesOnly = all with { HideFlashing = false };
        Assert.Equal(["TaskbarBadges"], FocusChanges.During(badgesOnly).Keys);
        Assert.Equal(["TaskbarBadges"], FocusChanges.After(badgesOnly, _ => null).Keys);
        Assert.Empty(FocusChanges.During(all with { HideBadges = false, HideFlashing = false }));
    }

    [Theory]
    [InlineData("ms-resource:FocusSessionCompletedTitle", "ms-resource://Microsoft.WindowsAlarms/Resources/FocusSessionCompletedTitle")]
    [InlineData("ms-resource:Strings/Title", "ms-resource://Microsoft.WindowsAlarms/Strings/Title")]
    [InlineData("ms-resource:///Resources/Title", "ms-resource://Microsoft.WindowsAlarms/Resources/Title")]
    [InlineData("ms-resource://Other.App/Resources/Title", "ms-resource://Other.App/Resources/Title")]
    [InlineData("Great job!", null)]
    public void Toast_texts_naming_app_resources_are_looked_up_in_the_package(string text, string? uri) =>
        Assert.Equal(uri, UserNotifications.ResourceUri(text, "Microsoft.WindowsAlarms"));

    [Fact]
    public void Toast_audio_is_read_from_the_audio_element_and_scenario()
    {
        XDocument toast = XDocument.Parse(
            "<toast scenario='alarm'><visual><binding template='ToastGeneric'><text>Wake</text></binding></visual>" +
            "<audio src=' ms-winsoundevent:Notification.Looping.Alarm2 ' loop='TRUE' silent='1'/>" +
            "<actions><action content='Snooze' arguments='snooze' activationType='system'/></actions></toast>");

        Assert.Equal(new ToastAudio("ms-winsoundevent:Notification.Looping.Alarm2", true, false, "alarm"), ToastAudio.Parse(toast));
        Assert.Equal(new ToastAudio(null, false, false, null), ToastAudio.Parse(XDocument.Parse("<toast><audio src=''/></toast>")));
    }

    [Fact]
    public void Toast_scenario_counts_only_with_a_button()
    {
        XDocument toast = XDocument.Parse("<toast scenario='incomingCall'><visual/><actions><input id='reply' type='text'/></actions></toast>");

        Assert.Null(ToastAudio.Parse(toast).Scenario);
    }

    [Fact]
    public void Toast_texts_from_xml_match_the_listeners_title_and_body()
    {
        XDocument generic = XDocument.Parse(
            "<toast><visual><binding template='ToastGeneric'><text>Title</text><text> </text><text>One</text><text>Two</text>" +
            "</binding></visual></toast>");
        XDocument legacy = XDocument.Parse(
            "<toast><visual><binding template='ToastText02'><text id='1'>Title</text><text id='2'>Body</text></binding></visual></toast>");

        Assert.Equal(("Title", "One\nTwo"), ToastAudio.Texts(generic));
        Assert.Equal(("Title", "Body"), ToastAudio.Texts(legacy));
        Assert.Equal(("", ""), ToastAudio.Texts(XDocument.Parse("<toast/>")));
    }

    [Theory]
    [InlineData(null, null, "ms-winsoundevent:Notification.Default", false)]
    [InlineData("ms-winsoundevent:Notification.Mail", null, "ms-winsoundevent:Notification.Mail", false)]
    [InlineData("ms-winsoundevent:Notification.Looping.Call2", "loop", "ms-winsoundevent:Notification.Looping.Call2", true)]
    [InlineData(null, "alarm", "ms-winsoundevent:Notification.Looping.Alarm", false)]
    [InlineData("ms-winsoundevent:Notification.Looping.Alarm5", "alarm", "ms-winsoundevent:Notification.Looping.Alarm5", false)]
    [InlineData("ms-winsoundevent:Notification.IM", "incomingCall", "ms-winsoundevent:Notification.Looping.Call", false)]
    [InlineData("ms-winsoundevent:Notification.Reminder", "reminder", "ms-winsoundevent:Notification.Reminder", false)]
    public void Toast_sound_is_its_own_or_the_default_and_alarms_and_calls_ring_once_unless_looped(string? source, string? scenario, string expected, bool loop)
    {
        var audio = new ToastAudio(source, scenario == "loop", false, scenario == "loop" ? null : scenario);

        Assert.Equal(new ToastSound(expected, loop), ToastSounds.Choose(audio, soundsAllowed: true, appSoundFile: null));
    }

    [Fact]
    public void Toast_sound_follows_the_settings()
    {
        var audio = new ToastAudio("ms-winsoundevent:Notification.SMS", false, false, null);

        Assert.Null(ToastSounds.Choose(audio, soundsAllowed: false, appSoundFile: null));
        Assert.Null(ToastSounds.Choose(audio with { Silent = true }, soundsAllowed: true, appSoundFile: null));
        Assert.Null(ToastSounds.Choose(ToastAudio.None, soundsAllowed: true, appSoundFile: null));
        Assert.Null(ToastSounds.Choose(audio, soundsAllowed: true, appSoundFile: ""));
        Assert.Equal(new ToastSound("ms-winsoundevent:Notification.SMS", false), ToastSounds.Choose(audio, true, "*default*"));
        Assert.Equal(new ToastSound("file:///C:/Windows/Media/chimes.wav", false), ToastSounds.Choose(audio, true, @"C:\Windows\Media\chimes.wav"));
    }

    [Theory]
    [InlineData("ms-winsoundevent:Notification.IM", "ms-winsoundevent:Notification.IM")]
    [InlineData("ms-appx:///Assets/Sounds/Ding%20Dong.wav", @"C:\Program Files\WindowsApps\App\Assets\Sounds\Ding Dong.wav")]
    [InlineData("ms-appdata:///local/ring.mp3", @"C:\Users\u\AppData\Local\Packages\App_x\LocalState\ring.mp3")]
    [InlineData("file:///C:/Media/beep.wav", @"C:\Media\beep.wav")]
    [InlineData("ms-appdata:///roaming/ring.mp3", null)]
    [InlineData(@"C:\Media\beep.wav", null)]
    [InlineData("https://example.com/beep.wav", null)]
    public void Toast_sound_files_are_found_where_the_platform_looks(string source, string? expected)
    {
        Assert.Equal(expected, NotificationSound.Locate(
            source, () => @"C:\Program Files\WindowsApps\App", () => @"C:\Users\u\AppData\Local\Packages\App_x\LocalState"));
    }

    [Fact]
    public void Package_relative_sound_files_of_an_unpackaged_app_get_the_default_sound()
    {
        Assert.Null(NotificationSound.Locate("ms-appx:///beep.wav", () => null, () => null));
    }
}
