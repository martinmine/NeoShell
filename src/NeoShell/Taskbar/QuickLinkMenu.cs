using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>
/// The Quick Link menu, which Explorer opens on a right-click of Start or with Win+X: the system's tools, everyday
/// places, the power options and the desktop, in Windows 11's order.
/// </summary>
internal static class QuickLinkMenu
{
    /// <param name="taskbar">The taskbar the menu belongs to; the Run dialog opens above its left end.</param>
    public static MenuFlyout Create(Taskbars owner, TaskbarWindow taskbar)
    {
        RunMode mode = owner.RunMode;
        // Windows names the item after Terminal when it's installed (its alias is on the path), else PowerShell.
        bool terminal = File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe"));
        string console = terminal ? "wt.exe" : "powershell.exe";
        string consoleName = terminal ? "Terminal" : "Windows PowerShell";

        var shutDown = new MenuFlyoutSubItem { Text = "Shut down or sign out" };
        AutomationProperties.SetAutomationId(shutDown, "QuickLinkPowerMenuItem");
        shutDown.Items.Add(Item("Sign out", "QuickLinkSignOutMenuItem", () => RunPowerAction("Sign out", Power.SignOut)));
        shutDown.Items.Add(Item("Sleep", "QuickLinkSleepMenuItem", () => RunPowerAction("Sleep", Power.Sleep)));
        shutDown.Items.Add(Item("Shut down", "QuickLinkShutDownMenuItem", () => RunPowerAction("Shut down", Power.ShutDown)));
        shutDown.Items.Add(Item("Restart", "QuickLinkRestartMenuItem", () => RunPowerAction("Restart", Power.Restart)));

        // As the shell, Settings can't start (it needs Explorer): its pages are Control Panel's applets instead.
        var menu = new MenuFlyout();
        foreach (MenuFlyoutItemBase item in (MenuFlyoutItemBase[])
        [
            Item("Installed apps", "QuickLinkAppsMenuItem", () => Launcher.OpenSettings(mode, "Installed apps", "ms-settings:appsfeatures", "appwiz.cpl")),
            Item("Power Options", "QuickLinkPowerOptionsMenuItem", () => Launcher.OpenSettings(mode, "Power Options", "ms-settings:powersleep", "powercfg.cpl")),
            Item("Event Viewer", "QuickLinkEventViewerMenuItem", () => Open("eventvwr.msc")),
            Item("System", "QuickLinkSystemMenuItem", () => Launcher.OpenSettings(mode, "System", "ms-settings:about", "sysdm.cpl")),
            Item("Device Manager", "QuickLinkDeviceManagerMenuItem", () => Open("devmgmt.msc")),
            Item("Network Connections", "QuickLinkNetworkMenuItem", () => Launcher.OpenSettings(mode, "Network Connections", "ms-settings:network", "ncpa.cpl")),
            Item("Disk Management", "QuickLinkDiskManagementMenuItem", () => Open("diskmgmt.msc")),
            Item("Computer Management", "QuickLinkComputerManagementMenuItem", () => Open("compmgmt.msc")),
            Item(consoleName, "QuickLinkTerminalMenuItem", () => Open(console)),
            Item($"{consoleName} (Admin)", "QuickLinkTerminalAdminMenuItem", () => Open(console, elevated: true)),
            new MenuFlyoutSeparator(),
            Item("Task Manager", "QuickLinkTaskManagerMenuItem", owner.OpenTaskManager),
            Item("Settings", "QuickLinkSettingsMenuItem", () => Launcher.OpenSettings(mode, "Settings", "ms-settings:")),
            Item("File Explorer", "QuickLinkFileExplorerMenuItem", Launcher.OpenFileExplorer),
            Item("Search", "QuickLinkSearchMenuItem", owner.OpenStartMenu),
            Item("Run", "QuickLinkRunMenuItem", () => ShellLaunch.ShowRunDialog(taskbar.ScreenBounds.X, taskbar.ScreenBounds.Y)),
            new MenuFlyoutSeparator(),
            shutDown,
            Item("Desktop", "QuickLinkDesktopMenuItem", owner.ToggleDesktop),
        ])
        {
            menu.Items.Add(item);
        }
        return menu;
    }

    private static MenuFlyoutItem Item(string text, string automationId, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => onClick();
        return item;
    }

    // Failures (a missing tool, a declined elevation) are logged, as Launcher does.
    private static void Open(string file, bool elevated = false)
    {
        try
        {
            ShellLaunch.Open(file, elevated: elevated);
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"Could not open {file}", ex);
        }
    }

    private static void RunPowerAction(string name, Action action)
    {
        Log.Info($"Power: {name}");
        try
        {
            action();
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"{name} failed", ex);
        }
    }
}
