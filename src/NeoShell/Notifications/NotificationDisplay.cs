using System.Globalization;
using NeoShell.Interop.Notifications;

namespace NeoShell.Notifications;

/// <summary>A notification center group: one app's notifications, newest first.</summary>
public sealed record NotificationGroup(string AppId, string AppName, IReadOnlyList<ToastInfo> Toasts);

/// <summary>How the notification center and the calendar present things, as Explorer does.</summary>
public static class NotificationDisplay
{
    public const int MinFocusMinutes = 5;
    public const int MaxFocusMinutes = 240;

    /// <summary>Notifications by app; the app with the newest notification first.</summary>
    public static IReadOnlyList<NotificationGroup> Group(IEnumerable<ToastInfo> toasts) =>
        [.. toasts
            .GroupBy(t => t.AppId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NotificationGroup(g.Key, g.First().AppName, [.. g.OrderByDescending(t => t.Time).ThenByDescending(t => t.Id)]))
            .OrderByDescending(g => g.Toasts[0].Time)];

    /// <summary>The time on a notification: just the time for today's, the date too for older ones.</summary>
    public static string TimeText(DateTimeOffset time, DateTime now, CultureInfo culture)
    {
        DateTime local = time.LocalDateTime;
        return local.Date == now.Date
            ? local.ToString("t", culture)
            : local.ToString("d", culture) + " " + local.ToString("t", culture);
    }

    /// <summary>"+3 notifications" under a collapsed group's newest one.</summary>
    public static string MoreText(int hidden) => hidden == 1 ? "+1 notification" : $"+{hidden} notifications";

    /// <summary>
    /// The calendar's heading, as Explorer's: the long date without the year ("Sunday, 4 October" in en-GB,
    /// "Sunday, October 4" in en-US).
    /// </summary>
    public static string DayHeading(DateTime date, CultureInfo culture) =>
        date.ToString(WithoutYear(culture.DateTimeFormat.LongDatePattern), culture);

    /// <summary>
    /// Takes the year out of a date pattern, with the separator that joins it to the rest (", " before it, or a
    /// literal suffix such as '年' after it).
    /// </summary>
    public static string WithoutYear(string pattern)
    {
        int start = pattern.IndexOf('y');
        if (start < 0)
            return pattern;

        int end = start;
        while (end < pattern.Length && pattern[end] == 'y')
            end++;
        // A quoted suffix belongs to the year ("yyyy'年'").
        if (end < pattern.Length && pattern[end] == '\'')
        {
            int close = pattern.IndexOf('\'', end + 1);
            end = close < 0 ? pattern.Length : close + 1;
        }
        // Separators before it ("d MMMM, yyyy"), or after it when it comes first ("yyyy. MMMM d.").
        while (start > 0 && IsSeparator(pattern[start - 1]))
            start--;
        if (start == 0)
        {
            while (end < pattern.Length && IsSeparator(pattern[end]))
                end++;
        }
        return (pattern[..start] + pattern[end..]).Trim();
    }

    private static bool IsSeparator(char c) => c is ' ' or ',' or '.' or '/' or '-';

    /// <summary>The next focus length up: 5 minutes at a time to half an hour, then 15 at a time.</summary>
    public static int MoreFocus(int minutes) => Math.Min(MaxFocusMinutes, minutes < 30 ? minutes + 5 : minutes + 15);

    public static int LessFocus(int minutes) => Math.Max(MinFocusMinutes, minutes <= 30 ? minutes - 5 : minutes - 15);

    /// <summary>What's left of a focus session: "24:59", or "1:04:59" for an hour or more.</summary>
    public static string RemainingText(TimeSpan remaining)
    {
        var seconds = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, remaining.TotalSeconds)));
        return seconds.TotalHours >= 1 ? seconds.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : seconds.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
