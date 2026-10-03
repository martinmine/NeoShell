using System.Diagnostics;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell;

internal static class Launcher
{
    /// <summary>
    /// Starts an app: through <c>shell:AppsFolder</c> when it has an AppUserModelID (which also covers packaged apps),
    /// otherwise its executable. Failures (missing file, cancelled elevation) are logged, not thrown.
    /// </summary>
    public static void Launch(PinnedApp app)
    {
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
}
