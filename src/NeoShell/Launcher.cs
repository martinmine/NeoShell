using System.Diagnostics;
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

        var startInfo = app.AppUserModelId is { } appId
            ? new ProcessStartInfo(ShellItems.AppsFolderPath(appId))
            : new ProcessStartInfo(app.Path ?? "") { Arguments = app.Arguments ?? "" };
        startInfo.UseShellExecute = true;

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start {app.DisplayName} ({startInfo.FileName})", ex);
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
