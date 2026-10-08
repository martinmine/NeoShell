using Microsoft.Win32;

namespace NeoShell.Snap;

/// <summary>
/// Settings → System → Multitasking → Snap windows, as Explorer reads them (each read when it's needed, so a change
/// counts at once). The checkboxes are on unless their value is 0; with "Snap windows" off none of them count.
/// </summary>
/// <param name="Enabled">"Snap windows": <c>WindowArrangementActive</c> under Control Panel\Desktop (Windows' own
/// <c>SPI_SETWINARRANGING</c>).</param>
/// <param name="Assist">"When I snap a window, suggest what I can snap next to it": <c>SnapAssist</c>.</param>
/// <param name="HoverFlyout">"Show snap layouts when I hover over a window's maximize button": <c>EnableSnapAssistFlyout</c>.</param>
/// <param name="SnapBar">"Show snap layouts when I drag a window to the top of my screen": <c>EnableSnapBar</c>.</param>
/// <param name="Groups">"Show my snapped windows when I hover over taskbar apps, in Task View, and when I press
/// Alt+Tab": <c>EnableTaskGroups</c>.</param>
/// <param name="NearEdge">"When I drag a window, let me snap it without dragging all the way to the screen edge":
/// <c>DITest</c>.</param>
public sealed record SnapSettings(bool Enabled, bool Assist, bool HoverFlyout, bool SnapBar, bool Groups, bool NearEdge)
{
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static SnapSettings Read()
    {
        using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(DesktopKey);
        using RegistryKey? advanced = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        return FromValues(desktop?.GetValue("WindowArrangementActive"), advanced?.GetValue("SnapAssist"),
            advanced?.GetValue("EnableSnapAssistFlyout"), advanced?.GetValue("EnableSnapBar"),
            advanced?.GetValue("EnableTaskGroups"), advanced?.GetValue("DITest"));
    }

    public static SnapSettings FromValues(object? arranging, object? assist, object? hoverFlyout, object? snapBar, object? groups, object? nearEdge)
    {
        // Windows writes "Snap windows" as a string ("1" or "0").
        bool enabled = arranging is not string text || text.Trim() != "0";
        return new SnapSettings(enabled, enabled && On(assist), enabled && On(hoverFlyout), enabled && On(snapBar),
            enabled && On(groups), enabled && On(nearEdge));
    }

    private static bool On(object? value) => value is not int number || number != 0;
}
