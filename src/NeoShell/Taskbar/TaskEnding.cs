using Microsoft.Win32;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>
/// "End task" in a task button's menu, as Explorer offers it with Settings' System &gt; For developers &gt; End task
/// on (JumpViewUI.dll's <c>EndTaskSystemItem</c>, windows.internal.shell.broker.dll's <c>CJumpViewBroker::EndTask</c>,
/// Taskbar.dll's <c>CTaskBand::HandleJumpViewEndTask</c>).
/// </summary>
public static class TaskEnding
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
    private const string FileExplorerAppId = "Microsoft.Windows.Explorer";

    /// <summary>
    /// Whether a button with windows offers it. Explorer reads the setting each time a menu opens, so a change applies
    /// at once; it never offers to end File Explorer, whose windows belong to the shell's own process.
    /// </summary>
    public static bool IsOffered(PinnedApp app)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return IsOffered(key?.GetValue("TaskbarEndTask"), app.AppUserModelId);
    }

    /// <summary>Only a DWORD of exactly 1 turns it on.</summary>
    public static bool IsOffered(object? taskbarEndTask, string? appUserModelId) =>
        taskbarEndTask is 1 && !string.Equals(appUserModelId, FileExplorerAppId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ends the app at once, with no prompt and no WM_CLOSE first: each window's process (hung and elevated ones
    /// too), or a UWP app's whole package. Child processes keep running, as in Explorer.
    /// </summary>
    public static void End(IReadOnlyList<WindowInfo> windows, string? appUserModelId) => Task.Run(() =>
    {
        try
        {
            if (EndsPackage(windows, appUserModelId))
            {
                PackagedApps.EndAll(appUserModelId!);
                return;
            }
            // A window gone by now (its process ended with another of its windows) fails quietly, as in Explorer.
            foreach (WindowInfo window in windows)
                TopLevelWindows.EndTask(window.Handle);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not end {appUserModelId}", ex);
        }
    });

    /// <summary>
    /// A UWP app's windows are ApplicationFrameHost's frames: Explorer's broker ends such an app through the
    /// immersive shell, by its package, rather than ending the host's process.
    /// </summary>
    public static bool EndsPackage(IReadOnlyList<WindowInfo> windows, string? appUserModelId) =>
        appUserModelId is not null
        && PackagedApps.IsPackagedAppId(appUserModelId)
        && windows.Any(window => window.ClassName == "ApplicationFrameWindow");
}
