using System.Globalization;

namespace NeoShell.Taskbar;

/// <summary>The notification bell beside the clock: its glyph (Segoe Fluent Icons), whether it's in the accent's text colour, and its tooltip.</summary>
public sealed record BellLook(string Glyph, bool Accent, string ToolTip);

/// <summary>How Explorer's taskbar clock and its notification bell present things (SystemTray.dll, Windows 11 25H2).</summary>
public static class ClockDisplay
{
    /// <summary>
    /// The clock's time: Explorer puts a ratio sign (U+2236), which sits centred between digits, for each colon when
    /// the user's locale writes only in Latin script (<c>LOCALE_SSCRIPTS</c> is "Latn;").
    /// </summary>
    public static string Time(string formatted, string scripts) =>
        scripts == "Latn;" ? formatted.Replace(':', '\u2236') : formatted;

    /// <summary>
    /// The clock's tooltip: the long date, a blank line, then the day and time here and in each additional clock.
    /// </summary>
    public static string ToolTip(string longDate, IEnumerable<(string Day, string Time, string Place)> clocks) =>
        longDate + "\n\n" + string.Join("\n", clocks.Select(c => $"{c.Day} {c.Time} ({c.Place})"));

    /// <summary>
    /// The bell, or null when it's hidden. It shows when "Show notification bell icon" is on, and always while Do not
    /// disturb is; filled when new notifications came since the notification center was last open, in the accent's
    /// colour unless Do not disturb is on (Explorer's <c>NotificationBadgeSystemTrayIconDataModel::RefreshIcon</c>).
    /// </summary>
    public static BellLook? Bell(bool showBell, bool doNotDisturb, int newCount)
    {
        if (!showBell && !doNotDisturb)
            return null;

        string count = newCount switch
        {
            0 => "No new notifications",
            1 => "1 new notification",
            _ => $"{newCount.ToString(CultureInfo.CurrentCulture)} new notifications",
        };
        return doNotDisturb
            ? new BellLook(newCount > 0 ? "\uF2A8" : "\uF285", false, $"{count} (Do not disturb on)")
            : new BellLook(newCount > 0 ? "\uF2A5" : "\uF2A3", newCount > 0, count);
    }
}
