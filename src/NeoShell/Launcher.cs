using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell;

internal static class Launcher
{
    /// <summary>
    /// Starts an app: a packaged app through the activation manager, another app with an AppUserModelID through
    /// <c>shell:AppsFolder</c>, otherwise its executable. Failures (missing file, cancelled elevation) are logged,
    /// not thrown.
    /// </summary>
    /// <param name="elevated">As administrator, asking first. Packaged apps can't be started that way without Explorer.</param>
    public static void Launch(PinnedApp app, bool elevated = false)
    {
        if (!elevated && app.AppUserModelId is { } packagedId && PackagedApps.IsPackagedAppId(packagedId))
        {
            // Activation waits for the app to start.
            Task.Run(() =>
            {
                try
                {
                    PackagedApps.Activate(packagedId);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not start {app.DisplayName} ({packagedId})", ex);
                }
            });
            return;
        }

        string file = app.AppUserModelId is { } appId ? ShellItems.AppsFolderPath(appId) : app.Path ?? "";
        try
        {
            ShellLaunch.Open(file, app.AppUserModelId is null ? app.Arguments : null, elevated);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start {app.DisplayName} ({file})", ex);
        }
    }

    /// <summary>
    /// File Explorer, as Win+E and the Quick Link menu open it. Not <c>explorer.exe</c> on its own: started without
    /// arguments while NeoShell is the shell, Explorer makes itself a second shell, taskbar and all.
    /// </summary>
    public static void OpenFileExplorer() =>
        Launch(new PinnedApp("File Explorer", AppUserModelId: "Microsoft.Windows.Explorer"));

    /// <summary>
    /// Opens a page of Settings, or as the shell a Control Panel applet (or Control Panel's home): Settings is a UWP
    /// app, and UWP windows can't show without Explorer.
    /// </summary>
    /// <param name="applet">Arguments for <c>control.exe</c>, such as <c>ncpa.cpl</c>.</param>
    public static void OpenSettings(RunMode runMode, string displayName, string settingsUri, string applet = "") =>
        Launch(runMode == RunMode.Shell
            ? new PinnedApp(displayName, Path: "control.exe", Arguments: applet)
            : new PinnedApp(displayName, Path: settingsUri));
}
