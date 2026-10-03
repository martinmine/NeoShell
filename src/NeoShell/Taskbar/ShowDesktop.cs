using NeoShell.Interop.Windowing;
using NeoShell.Logging;

namespace NeoShell.Taskbar;

/// <summary>
/// The show-desktop button: minimizes every window, and a second click restores the ones it minimized.
/// Explorer offers this as a shell command, but NeoShell has to work without Explorer.
/// </summary>
internal sealed class ShowDesktop
{
    private List<nint> _minimized = [];

    public void Toggle()
    {
        // Top first, so restoring in reverse puts the original top window back on top.
        List<nint> toRestore = [.. _minimized.Where(TopLevelWindows.IsMinimized)];
        _minimized = [];
        if (toRestore.Count > 0)
        {
            for (int i = toRestore.Count - 1; i >= 0; i--)
                TopLevelWindows.Restore(toRestore[i]);
            Log.Info($"Show desktop: restored {toRestore.Count} window(s)");
            return;
        }

        int ownProcess = Environment.ProcessId;
        foreach (nint hwnd in TopLevelWindows.GetAll())
        {
            if (TopLevelWindows.CanMinimize(hwnd) && TopLevelWindows.GetProcessId(hwnd) != ownProcess)
            {
                TopLevelWindows.Minimize(hwnd);
                _minimized.Add(hwnd);
            }
        }
        Log.Info($"Show desktop: minimized {_minimized.Count} window(s)");
    }
}
