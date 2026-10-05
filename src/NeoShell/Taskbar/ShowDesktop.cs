using NeoShell.Interop.Windowing;
using NeoShell.Logging;

namespace NeoShell.Taskbar;

/// <summary>
/// Minimizing every window and bringing them back: the show-desktop button (and Win+D), Win+M, Win+Shift+M and
/// Win+Home. Explorer offers these as shell commands, but NeoShell has to work without Explorer.
/// </summary>
internal sealed class ShowDesktop
{
    // Top first, so restoring in reverse puts the original top window back on top.
    private readonly List<nint> _minimized = [];

    /// <summary>The show-desktop button and Win+D: minimizes every window, or restores the ones it minimized.</summary>
    public void Toggle()
    {
        if (!Restore())
            Minimize(keep: 0);
    }

    /// <summary>Win+M: minimizes every window.</summary>
    public void MinimizeAll() => Minimize(keep: 0);

    /// <summary>
    /// Win+Home: minimizes every window but <paramref name="keep"/>, or restores the ones it minimized behind it.
    /// </summary>
    public void ToggleAllBut(nint keep)
    {
        if (!Restore(activate: false))
            Minimize(keep);
    }

    /// <summary>
    /// Win+Shift+M: restores the windows minimized here that are still minimized; false when there were none.
    /// </summary>
    /// <param name="activate">False keeps the window in front in front.</param>
    public bool Restore(bool activate = true)
    {
        List<nint> toRestore = [.. _minimized.Where(TopLevelWindows.IsMinimized)];
        _minimized.Clear();
        for (int i = toRestore.Count - 1; i >= 0; i--)
            TopLevelWindows.Restore(toRestore[i], activate);
        if (toRestore.Count > 0)
            Log.Info($"Show desktop: restored {toRestore.Count} window(s)");
        return toRestore.Count > 0;
    }

    // Adds to the windows minimized before: Win+M twice, then Win+Shift+M, brings them all back.
    private void Minimize(nint keep)
    {
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
