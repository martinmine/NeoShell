using System.Globalization;

namespace NeoShell.Widgets;

/// <param name="InMonth">The day is in the month shown, not one of the days before or after it that fill its weeks.</param>
public sealed record CalendarDay(DateOnly Date, bool InMonth, bool IsToday)
{
    public string Number => Date.Day.ToString(CultureInfo.CurrentCulture);
}

/// <summary>The calendar widget's month: six weeks, so it keeps its height from month to month, as Vista's did.</summary>
public static class CalendarMonth
{
    public const int Weeks = 6;

    /// <summary>
    /// The days of the six weeks that show the month of <paramref name="shown"/>: from the week's first day on or
    /// before the 1st.
    /// </summary>
    public static CalendarDay[] Days(DateOnly shown, DayOfWeek firstDayOfWeek, DateOnly today)
    {
        var first = new DateOnly(shown.Year, shown.Month, 1);
        DateOnly start = first.AddDays(-(((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
        var days = new CalendarDay[Weeks * 7];
        for (int i = 0; i < days.Length; i++)
        {
            DateOnly date = start.AddDays(i);
            days[i] = new CalendarDay(date, date.Month == shown.Month, date == today);
        }
        return days;
    }

    /// <summary>
    /// The weekdays' names in two letters, from the week's first day on, as Windows' calendar writes them: "Mo", "Tu"…
    /// (.NET's shortest names are one letter in English, "M", "T", and two days would read the same.)
    /// </summary>
    public static string[] WeekdayNames(DateTimeFormatInfo format)
    {
        var names = new string[7];
        for (int i = 0; i < 7; i++)
        {
            string name = format.AbbreviatedDayNames[((int)format.FirstDayOfWeek + i) % 7];
            names[i] = name.Length > 2 ? name[..2] : name;
        }
        return names;
    }
}
