namespace NeoShell.StartMenu;

/// <summary>A command of an app's (or a folder's) menu in Start.</summary>
public enum AppCommand
{
    MoveToFront,
    MoveLeft,
    MoveRight,
    NewFolder,
    MoveToFolder,
    RemoveFromFolder,
    PinToStart,
    UnpinFromStart,
    RunAsAdministrator,
    OpenFileLocation,
    Manage,
    ItemProperties,
    MapNetworkDrive,
    DisconnectNetworkDrive,
    PinToTaskbar,
    UnpinFromTaskbar,
    AppSettings,
    Uninstall,
}

/// <summary>
/// How Start lays out an app's menu, as Explorer's does (<c>StartMenu.dll</c>'s <c>ContextMenuSorter</c>): the
/// commands in a fixed order, in groups parted by separators; a group with nothing in it leaves no separator. The
/// app's jump list goes below, without one.
/// </summary>
public static class StartAppMenu
{
    private static readonly AppCommand[][] s_groups =
    [
        [AppCommand.MoveToFront, AppCommand.MoveLeft, AppCommand.MoveRight, AppCommand.NewFolder, AppCommand.MoveToFolder,
            AppCommand.RemoveFromFolder, AppCommand.PinToStart, AppCommand.UnpinFromStart],
        [AppCommand.RunAsAdministrator, AppCommand.OpenFileLocation],
        [AppCommand.Manage, AppCommand.ItemProperties, AppCommand.MapNetworkDrive, AppCommand.DisconnectNetworkDrive],
        [AppCommand.PinToTaskbar, AppCommand.UnpinFromTaskbar, AppCommand.AppSettings, AppCommand.Uninstall],
    ];

    // Windows' search box lists an app's commands in one run, in an order of its own.
    private static readonly AppCommand[] s_searchOrder =
    [
        AppCommand.RunAsAdministrator, AppCommand.OpenFileLocation, AppCommand.PinToStart, AppCommand.UnpinFromStart,
        AppCommand.PinToTaskbar, AppCommand.UnpinFromTaskbar, AppCommand.AppSettings, AppCommand.Uninstall,
    ];

    // The verbs of an app's menu in shell:AppsFolder that Start shows; the folder's others (Open, Open new window,
    // Create shortcut, its own pinning) it leaves out.
    private static readonly Dictionary<string, AppCommand> s_verbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["runas"] = AppCommand.RunAsAdministrator,
        ["OpenFileLocation"] = AppCommand.OpenFileLocation,
        ["Manage"] = AppCommand.Manage,
        ["ItemProperties"] = AppCommand.ItemProperties,
        ["connectNetworkDrive"] = AppCommand.MapNetworkDrive,
        ["disconnectNetworkDrive"] = AppCommand.DisconnectNetworkDrive,
        ["Uninstall"] = AppCommand.Uninstall,
    };

    /// <summary>The commands present, in Start's order; null stands for a separator.</summary>
    public static IReadOnlyList<AppCommand?> Layout(IReadOnlySet<AppCommand> commands)
    {
        var layout = new List<AppCommand?>();
        foreach (AppCommand[] group in s_groups)
        {
            AppCommand[] present = [.. group.Where(commands.Contains)];
            if (present.Length == 0)
                continue;
            if (layout.Count > 0)
                layout.Add(null);
            layout.AddRange(present.Select(command => (AppCommand?)command));
        }
        return layout;
    }

    /// <summary>The commands present, in the order Windows' search results list them, without separators.</summary>
    public static IReadOnlyList<AppCommand> SearchLayout(IReadOnlySet<AppCommand> commands) => [.. s_searchOrder.Where(commands.Contains)];

    /// <summary>The command a verb of the app's shell menu stands for in Start; null for verbs Start leaves out.</summary>
    public static AppCommand? FromVerb(string? verb) => verb is not null && s_verbs.TryGetValue(verb, out AppCommand command) ? command : null;
}
