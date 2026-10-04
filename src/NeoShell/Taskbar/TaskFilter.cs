using NeoShell.Interop.Windowing;

namespace NeoShell.Taskbar;

/// <summary>Which windows get a taskbar button, by Explorer's rules.</summary>
public static class TaskFilter
{
    // A top-level CoreWindow is a shell surface such as Explorer's Start or Search; a UWP app's button belongs to its
    // ApplicationFrameWindow.
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    public static bool GetsButton(WindowInfo window, int ownProcessId) =>
        window.IsVisible
        && !window.IsCloaked // other virtual desktops, suspended UWP apps
        && window.ProcessId != ownProcessId
        && window.ClassName != CoreWindowClass
        && (window.IsAppWindow || (window.Owner == 0 && !window.IsToolWindow && !window.IsNoActivate));
}
