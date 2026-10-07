using System.Globalization;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// The time as Explorer's taskbar clock writes it (SystemTray.dll, <c>ClockSystemTrayIconDataModel2::GetTimeString</c>):
/// <c>GetTimeFormatEx</c> with the user's own format, which is Region's long time format (with seconds), dropping
/// the seconds and the separator before them unless they're wanted.
/// </summary>
public static class RegionalTime
{
    public static unsafe string Format(DateTime time, bool seconds)
    {
        var systemTime = new Kernel32.SYSTEMTIME
        {
            Year = (ushort)time.Year,
            Month = (ushort)time.Month,
            DayOfWeek = (ushort)time.DayOfWeek,
            Day = (ushort)time.Day,
            Hour = (ushort)time.Hour,
            Minute = (ushort)time.Minute,
            Second = (ushort)time.Second,
            Milliseconds = (ushort)time.Millisecond,
        };
        const int Size = 128;
        char* buffer = stackalloc char[Size];
        int length = Kernel32.GetTimeFormatEx(null, seconds ? 0 : Kernel32.TIME_NOSECONDS, systemTime, null, buffer, Size);
        return length > 0 ? new string(buffer, 0, length - 1) : time.ToString(seconds ? "T" : "t", CultureInfo.CurrentCulture);
    }

    /// <summary>The scripts the user's locale writes in, as <c>LOCALE_SSCRIPTS</c> lists them: "Latn;" for English.</summary>
    public static unsafe string Scripts()
    {
        const int Size = 64;
        char* buffer = stackalloc char[Size];
        int length = Kernel32.GetLocaleInfoEx(null, Kernel32.LOCALE_SSCRIPTS, buffer, Size);
        return length > 0 ? new string(buffer, 0, length - 1) : "";
    }
}
