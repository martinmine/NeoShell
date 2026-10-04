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
    public static void Launch(PinnedApp app)
    {
        if (app.AppUserModelId is { } packagedId && PackagedApps.IsPackagedAppId(packagedId))
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
            ShellLaunch.Open(file, app.AppUserModelId is null ? app.Arguments : null);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start {app.DisplayName} ({file})", ex);
        }
    }

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
