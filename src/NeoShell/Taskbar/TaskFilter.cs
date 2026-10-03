using NeoShell.Interop.Windowing;

namespace NeoShell.Taskbar;

/// <summary>Which windows get a taskbar button, by Explorer's rules.</summary>
public static class TaskFilter
{
    public static bool GetsButton(WindowInfo window, int ownProcessId) =>
        window.IsVisible
        && !window.IsCloaked // other virtual desktops, suspended UWP apps
        && window.ProcessId != ownProcessId
        && (window.IsAppWindow || (window.Owner == 0 && !window.IsToolWindow && !window.IsNoActivate));
}
