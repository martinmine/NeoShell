using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Taskbar;

namespace NeoShell;

/// <summary>What the Copilot key does, by Settings → Personalization → Text input → "Customize Copilot key".</summary>
public abstract record CopilotKeyTarget
{
    private CopilotKeyTarget() { }

    /// <summary>Brings the app up, or down if it's in front.</summary>
    public sealed record App(string AppUserModelId) : CopilotKeyTarget;

    /// <summary>Search: Start with its search box, as Win+S.</summary>
    public sealed record Search : CopilotKeyTarget;

    /// <summary>Nothing chosen (or the choice failed): Explorer then shows the setting's page, or search.</summary>
    public sealed record Unset : CopilotKeyTarget;
}

/// <summary>
/// The Copilot key as the shell, as Explorer's (twinui.pcshell <c>CopilotHotkeyManager::InvokeCopilotOrCustomOption</c>):
/// the choice in <c>HKCU\Software\Microsoft\Windows\Shell\BrandedKey</c> picks an app or search.
/// </summary>
public static class CopilotKey
{
    public const string KeyPath = @"Software\Microsoft\Windows\Shell\BrandedKey";

    /// <summary>The Copilot app, which the key opens when nothing is chosen.</summary>
    public const string CopilotAppId = "Microsoft.Copilot_8wekyb3d8bbwe!App";

    /// <summary>
    /// Explorer's reading of the setting: <c>BrandedKeyChoiceType</c> "App" (or "AppEnforcedByPolicy", written for the
    /// <c>SetCopilotHardwareKey</c> policy) opens <c>AppAumid</c>, "Search" searches; with no key at all, the Copilot
    /// app. Anything else counts as unset.
    /// </summary>
    /// <param name="keyExists">Whether the BrandedKey key exists (Settings creates it with the first choice).</param>
    public static CopilotKeyTarget Choose(bool keyExists, string? choiceType, string? appAumid) =>
        !keyExists ? new CopilotKeyTarget.App(CopilotAppId)
        : choiceType is "App" or "AppEnforcedByPolicy" && !string.IsNullOrEmpty(appAumid) ? new CopilotKeyTarget.App(appAumid)
        : choiceType == "Search" ? new CopilotKeyTarget.Search()
        : new CopilotKeyTarget.Unset();

    public static CopilotKeyTarget Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return Choose(key is not null, key?.GetValue("BrandedKeyChoiceType") as string, key?.GetValue("AppAumid") as string);
    }

    /// <summary>
    /// A press of the key. An app's open window comes to the front, or is minimized if it's in front already;
    /// otherwise the app is started. Explorer falls back to the setting's page in Settings (a UWP app, which can't
    /// show without Explorer) or to search; NeoShell searches.
    /// </summary>
    internal static void Press(Taskbars taskbars)
    {
        CopilotKeyTarget target = Read();
        Log.Info($"Copilot key: {target}");
        if (target is not CopilotKeyTarget.App app)
        {
            taskbars.ToggleStartSearch();
            return;
        }

        if (WindowOf(taskbars.Tracker, app.AppUserModelId) is { } window)
        {
            if (window == TopLevelWindows.GetForeground())
                TopLevelWindows.MinimizeAndActivateNext(window);
            else
                TopLevelWindows.SwitchTo(window);
            return;
        }

        // Activation waits for the app to start.
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        Task.Run(() =>
        {
            try
            {
                if (PackagedApps.IsPackagedAppId(app.AppUserModelId))
                    PackagedApps.Activate(app.AppUserModelId);
                else
                    ShellLaunch.Open(ShellItems.AppsFolderPath(app.AppUserModelId));
            }
            catch (Exception ex)
            {
                Log.Warn($"Copilot key: could not start {app.AppUserModelId}", ex);
                dispatcher.Post(() =>
                {
                    // The key's been let go of by now, so the app in front has the last input and with it the
                    // foreground; a key of NeoShell's own lets Start take it, as SwitchTo does.
                    KeyboardHook.MaskModifierKeys();
                    taskbars.ToggleStartSearch();
                });
            }
        });
    }

    // The app's window used last, as Explorer finds the app's view.
    private static nint? WindowOf(WindowTracker tracker, string appUserModelId)
    {
        var windows = tracker.Windows
            .Where(w => string.Equals(w.AppUserModelId, appUserModelId, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Handle)
            .ToList();
        if (windows.Count == 0)
            return null;
        return tracker.RecentlyActive.FirstOrDefault(windows.Contains) is var recent and not 0 ? recent : windows[0];
    }
}
