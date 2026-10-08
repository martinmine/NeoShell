using NeoShell.Interop.Windowing;

namespace NeoShell.Taskbar;

/// <summary>Which windows get a taskbar button, and whether a flashing one shows it, by Explorer's rules.</summary>
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

    /// <summary>
    /// "Show flashing on taskbar apps" (<c>TaskbarFlashing</c> under Explorer\Advanced, which a focus session turns
    /// off): on unless it's 0.
    /// </summary>
    public static bool ShowsFlashing(object? taskbarFlashing) => taskbarFlashing is not int value || value != 0;
}
