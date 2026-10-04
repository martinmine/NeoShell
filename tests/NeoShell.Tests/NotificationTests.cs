using System.Globalization;
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

    [Theory]
    [InlineData(1499.2, "25:00")]
    [InlineData(59, "00:59")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-3, "00:00")]
    public void Focus_time_left_counts_down_in_minutes_and_seconds(double seconds, string expected)
    {
        Assert.Equal(expected, NotificationDisplay.RemainingText(TimeSpan.FromSeconds(seconds)));
    }
}
