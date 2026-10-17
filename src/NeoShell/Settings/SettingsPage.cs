namespace NeoShell.Settings;

/// <summary>The pages of NeoShell's Settings window, in the order its navigation lists them.</summary>
public enum SettingsPage { Taskbar, Start, Desktop, Widgets, WindowFrames, Agents, About }

public static class SettingsPages
{
    public static string Title(SettingsPage page) => page switch
    {
        SettingsPage.Taskbar => "Taskbar",
        SettingsPage.Start => "Start",
        SettingsPage.Desktop => "Desktop and wallpaper",
        SettingsPage.Widgets => "Widgets",
        SettingsPage.WindowFrames => "Window frames",
        SettingsPage.Agents => "Agents",
        _ => "About",
    };

    /// <summary>Segoe Fluent Icons glyph in the navigation.</summary>
    public static string Glyph(SettingsPage page) => page switch
    {
        SettingsPage.Taskbar => "",
        SettingsPage.Start => "",
        SettingsPage.Desktop => "",
        SettingsPage.Widgets => "",
        SettingsPage.WindowFrames => "",
        SettingsPage.Agents => "",
        _ => "",
    };

    /// <summary>
    /// NeoShell's version as About shows it: the build's informational version (<c>1.0.0+&lt;commit&gt;</c>) with the
    /// commit cut to the 7 characters git shows.
    /// </summary>
    public static string Version(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
            return "Unknown";
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return informationalVersion;
        string commit = informationalVersion[(plus + 1)..];
        return $"{informationalVersion[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    /// <summary>The command that adds NeoShell to Claude Code as an MCP server (user scope: every project).</summary>
    public static string ClaudeCodeCommand(string neoShellPath) => $"claude mcp add --scope user neoshell -- \"{neoShellPath}\" /mcp";

    public static string NoAgentsText(bool accessOn) =>
        accessOn ? "No agents are connected." : "Agents can't connect while agent access is off.";

    public static string RunModeTitle(RunMode runMode) =>
        runMode == RunMode.Shell ? "As the shell" : "Alongside Explorer";

    public static string RunModeDescription(RunMode runMode) => runMode == RunMode.Shell
        ? "NeoShell is your shell: it shows the desktop, the taskbar with the system tray, and handles the Windows key."
        : "Explorer is your shell: NeoShell adds its taskbar, Start and widgets next to Explorer's.";
}
