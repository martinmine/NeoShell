using Microsoft.Win32;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;

namespace NeoShell.Taskbar;

/// <summary>
/// Minimizing every window and bringing them back: the show-desktop button (and Win+D), Win+M, Win+Shift+M, Win+Home
/// and shaking a window's title bar. Explorer offers these as shell commands, but NeoShell has to work without Explorer.
/// </summary>
public sealed class ShowDesktop
{
    // Top first, so restoring in reverse puts the original top window back on top.
    private readonly List<nint> _minimized = [];
    // The window Win+Home or a shake left up, which the same again with it restores the others for.
    private nint _keptUp;

    /// <summary>The show-desktop button and Win+D: minimizes every window, or restores the ones it minimized.</summary>
    public void Toggle()
    {
        if (!Restore())
            Minimize(keep: 0);
    }

    /// <summary>Win+M: minimizes every window.</summary>
    public void MinimizeAll() => Minimize(keep: 0);

    /// <summary>
    /// Win+Home and title bar shake: minimizes every window but <paramref name="keep"/> (and its owners); the same
    /// again for the same window restores the ones it minimized behind it. Another window minimizes all but that one.
    /// </summary>
    public void ToggleAllBut(nint keep)
    {
        if (keep != 0 && keep == _keptUp)
        {
            _keptUp = 0;
            // Behind the window kept up, as Explorer puts them back.
            Restore(activate: false, below: keep);
            return;
        }
        Minimize(TopLevelWindows.RootOwner(keep));
        _keptUp = keep;
    }

    /// <summary>
    /// Whether shaking a title bar minimizes the other windows: Settings → System → Multitasking → "Title bar window
    /// shake" (<c>DisallowShaking</c>, off unless 0) and the policy "Turn off Aero Shake window minimizing mouse
    /// gesture". Read each time, as Explorer does.
    /// </summary>
    public static bool ShakingAllowed()
    {
        const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string PolicyKey = @"Software\Policies\Microsoft\Windows\Explorer";
        using RegistryKey? advanced = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        using RegistryKey? userPolicy = Registry.CurrentUser.OpenSubKey(PolicyKey);
        using RegistryKey? machinePolicy = Registry.LocalMachine.OpenSubKey(PolicyKey);
        return ShakingAllowed(advanced?.GetValue("DisallowShaking"),
            userPolicy?.GetValue("NoWindowMinimizingShortcuts") ?? machinePolicy?.GetValue("NoWindowMinimizingShortcuts"));
    }

    public static bool ShakingAllowed(object? disallowShaking, object? noMinimizingShortcuts) =>
        disallowShaking is 0 && noMinimizingShortcuts is null or 0;

    /// <summary>
    /// Win+Shift+M: restores the windows minimized here that are still minimized; false when there were none.
    /// </summary>
    /// <param name="activate">False keeps the window in front in front.</param>
    /// <param name="below">The window to restore them behind, which stays on top.</param>
    public bool Restore(bool activate = true, nint below = 0)
    {
        List<nint> toRestore = [.. _minimized.Where(TopLevelWindows.IsMinimized)];
        _minimized.Clear();
        for (int i = toRestore.Count - 1; i >= 0; i--)
            TopLevelWindows.Restore(toRestore[i], activate, below);
        if (toRestore.Count > 0)
            Log.Info($"Show desktop: restored {toRestore.Count} window(s)");
        return toRestore.Count > 0;
    }

    // Adds to the windows minimized before: Win+M twice, then Win+Shift+M, brings them all back.
    private void Minimize(nint keep)
    {
        _keptUp = 0;
        int ownProcess = Environment.ProcessId;
        int count = 0;
        foreach (nint hwnd in TopLevelWindows.GetAll())
        {
            if (hwnd != keep && TopLevelWindows.CanMinimize(hwnd) && TopLevelWindows.GetProcessId(hwnd) != ownProcess)
            {
                TopLevelWindows.Minimize(hwnd);
                _minimized.Add(hwnd);
                count++;
            }
        }
        Log.Info($"Show desktop: minimized {count} window(s)");
    }
}
