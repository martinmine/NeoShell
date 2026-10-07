using Microsoft.Win32;

namespace NeoShell.Taskbar;

/// <summary>A second clock for the clock's tooltip (Settings → Time &amp; language → Date &amp; time → Additional clocks).</summary>
public sealed record AdditionalClock(string Name, TimeZoneInfo Zone);

/// <summary>
/// What Explorer's taskbar clock shows, from the user's settings (SystemTray.dll, <c>ClockSystemTrayIconDataModel2</c>
/// and <c>NotificationBadgeSystemTrayIconDataModel</c>), which Explorer follows as they change.
/// </summary>
/// <param name="ShowClock">"Show time and date in the System tray": <c>ShowSystrayDateTimeValueName</c>, on unless 0.</param>
/// <param name="ShowSeconds">"Show seconds in system tray clock": <c>ShowSecondsInSystemClock</c>, off unless set.</param>
/// <param name="ShowBell">"Show notification bell icon": <c>ShowNotificationIcon</c>, off unless set.</param>
public sealed record ClockSettings(bool ShowClock, bool ShowSeconds, bool ShowBell, IReadOnlyList<AdditionalClock> AdditionalClocks)
{
    public const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string AdditionalClocksKey = @"Control Panel\TimeDate\AdditionalClocks";

    public static ClockSettings Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey);
        return FromValues(
            key?.GetValue("ShowSystrayDateTimeValueName"), key?.GetValue("ShowSecondsInSystemClock"), key?.GetValue("ShowNotificationIcon"),
            ReadAdditionalClocks());
    }

    public static ClockSettings FromValues(object? showClock, object? showSeconds, object? showBell, IReadOnlyList<AdditionalClock> additionalClocks) =>
        new(showClock is not int clock || clock != 0, showSeconds is int seconds && seconds != 0, showBell is int bell && bell != 0, additionalClocks);

    // Explorer's two: keys 1 and 2, each with Enable, DisplayName and the time zone's registry name.
    private static IReadOnlyList<AdditionalClock> ReadAdditionalClocks()
    {
        var clocks = new List<AdditionalClock>();
        foreach (string number in (string[])["1", "2"])
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{AdditionalClocksKey}\{number}");
            if (key?.GetValue("Enable") is not int enable || enable == 0 || key.GetValue("TzRegKeyName") is not string zoneId)
                continue;
            try
            {
                clocks.Add(new AdditionalClock(key.GetValue("DisplayName") as string ?? "", TimeZoneInfo.FindSystemTimeZoneById(zoneId)));
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }
        return clocks;
    }
}
