using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.StartMenu;
using NeoShell.Taskbar;

namespace NeoShell.Themes;

/// <summary>The themes, as the Settings window lists them (see docs/design/themes.md).</summary>
public static class ShellThemes
{
    /// <summary>Every theme, in the order Settings lists them.</summary>
    public static IReadOnlyList<ShellTheme> All { get; } = Enum.GetValues<ShellTheme>();

    public static string Name(ShellTheme theme) => theme switch
    {
        ShellTheme.WindowsXP => "Windows XP",
        ShellTheme.Windows98 => "Windows 98",
        ShellTheme.Concept2088 => "2088 (concept)",
        _ => "Windows 11",
    };
}

/// <summary>
/// The views each theme gives the surfaces it replaces, the taskbar and Start. Everything underneath them, and every
/// other surface, is the same in every theme.
/// </summary>
internal static class ThemeViews
{
    public static ITaskbarView CreateTaskbar(ShellTheme theme, Taskbars owner, DisplayMonitor monitor, ShellSettings settings) => theme switch
    {
        ShellTheme.WindowsXP => new WindowsXp.XpTaskbarWindow(owner, monitor, settings),
        ShellTheme.Windows98 => new Windows98.Win98TaskbarWindow(owner, monitor, settings),
        ShellTheme.Concept2088 => new Concept2088.SpineTaskbarWindow(owner, monitor, settings),
        _ => new TaskbarWindow(owner, monitor, settings, owner.Theme, owner.Accent),
    };

    public static IStartMenuView CreateStartMenu(ShellTheme theme, Taskbars owner) => theme switch
    {
        ShellTheme.WindowsXP => new WindowsXp.XpStartMenuWindow(owner),
        ShellTheme.Windows98 => new Windows98.Win98StartMenuWindow(owner),
        ShellTheme.Concept2088 => new Concept2088.NexusStartMenu(owner),
        _ => new StartMenuWindow(owner),
    };
}
