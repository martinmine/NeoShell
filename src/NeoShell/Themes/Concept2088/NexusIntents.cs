namespace NeoShell.Themes.Concept2088;

/// <summary>What Nexus's ask line does besides starting apps.</summary>
public enum NexusCommand { Lock, Sleep, Restart, ShutDown, SignOut, Settings, Run, Desktop }

/// <summary>
/// The words Nexus's ask line takes as commands (see docs/design/theme-2088.md), in English and Japanese. A command
/// is offered from the first two letters of one of its words; it comes before the apps only when the word is typed
/// whole, so Enter on a half-typed app name never restarts the PC.
/// </summary>
public static class NexusIntents
{
    private static readonly (NexusCommand Command, string Text, string[] Words)[] s_commands =
    [
        (NexusCommand.Lock, "LOCK THIS TERMINAL", ["lock", "施錠"]),
        (NexusCommand.Sleep, "SUSPEND // SLEEP", ["sleep", "suspend", "休止"]),
        (NexusCommand.Restart, "REBOOT SYSTEM", ["restart", "reboot", "再起動"]),
        (NexusCommand.ShutDown, "SHUT DOWN SYSTEM", ["shut down", "shutdown", "power off", "停止"]),
        (NexusCommand.SignOut, "SIGN OUT OPERATOR", ["sign out", "log off", "logout", "退出"]),
        (NexusCommand.Settings, "NEOSHELL CONFIG", ["settings", "config", "設定"]),
        (NexusCommand.Run, "RUN A COMMAND LINE", ["run", "execute", "実行"]),
        (NexusCommand.Desktop, "SHOW THE FIELD // DESKTOP", ["desktop", "field"]),
    ];

    /// <summary>The commands <paramref name="query"/> asks for, in their order; Exact when it is one of their words.</summary>
    public static IReadOnlyList<(NexusCommand Command, string Text, bool Exact)> Match(string query)
    {
        query = query.Trim();
        if (query.Length < 2)
            return [];
        return [.. s_commands
            .Where(command => command.Words.Any(word => word.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)))
            .Select(command => (command.Command, command.Text,
                command.Words.Any(word => word.Equals(query, StringComparison.CurrentCultureIgnoreCase))))];
    }
}

/// <summary>The 2088 concept's clock faces: its date, the weekday in Japanese, the uptime readout.</summary>
public static class Clock2088
{
    /// <summary>Today's date as a readout, year first with dots: <c>2026.10.13</c>. The real year: a clock is read.</summary>
    public static string Date(DateTime now) => now.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The weekday as Japanese calendars print it: 日 月 火 水 木 金 土.</summary>
    public static string Weekday(DateTime now) => "日月火水木金土"[(int)now.DayOfWeek].ToString();

    /// <summary>How long Windows has been up, as a readout: <c>3D 04:12:09</c>.</summary>
    public static string Uptime(TimeSpan up) => $"{up.Days}D {up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}";
}
