namespace NeoShell.Widgets;

/// <summary>What the clock widget shows: its hands' angles, the time in its zone and how that differs from here.</summary>
public static class ClockFace
{
    /// <summary>
    /// The hands' angles in degrees clockwise from twelve. The hour and minute hands move on between their marks; the
    /// second hand jumps from second to second.
    /// </summary>
    public static (double Hour, double Minute, double Second) Angles(TimeSpan timeOfDay)
    {
        double minutes = timeOfDay.Minutes + timeOfDay.Seconds / 60.0;
        double hours = timeOfDay.Hours % 12 + minutes / 60;
        return (hours * 30, minutes * 6, timeOfDay.Seconds * 6);
    }

    /// <summary>The time in a zone (Windows' id); the computer's for none, or for a zone Windows doesn't have.</summary>
    public static DateTime TimeIn(string? timeZoneId, DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone(timeZoneId));

    public static TimeZoneInfo Zone(string? timeZoneId)
    {
        if (timeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out TimeZoneInfo? zone))
            return zone;
        return TimeZoneInfo.Local;
    }

    /// <summary>
    /// How a zone's time differs from the computer's, as Windows' Clock app writes it: "Today, 6 hours ahead",
    /// "Yesterday, 9 hours behind", "Same time as here".
    /// </summary>
    public static string Difference(DateTime there, DateTime here)
    {
        TimeSpan offset = there - here;
        // The zones' offsets are whole minutes; the two times were read a moment apart.
        offset = TimeSpan.FromMinutes(Math.Round(offset.TotalMinutes));
        if (offset == TimeSpan.Zero)
            return "Same time as here";

        string day = (there.Date - here.Date).Days switch
        {
            > 0 => "Tomorrow",
            < 0 => "Yesterday",
            _ => "Today",
        };
        TimeSpan size = offset.Duration();
        string amount = size.Minutes == 0
            ? size.Hours == 1 ? "1 hour" : $"{size.Hours} hours"
            : $"{size.Hours}:{size.Minutes:00} hours";
        return $"{day}, {amount} {(offset > TimeSpan.Zero ? "ahead" : "behind")}";
    }
}
