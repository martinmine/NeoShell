using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.QuickSettings;
using NeoShell.Settings;
using NeoShell.Tray;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Themes;

/// <summary>
/// A theme's taskbar on one monitor, as <see cref="Taskbar.Taskbars"/> uses it: Taskbars keeps what every taskbar
/// shares (the tracked windows, pinned apps, tray, indicators, notifications, Start, hotkeys) and asks the view to show
/// it and to act on keys and changes. A view does what its Windows did; where that Windows had no such thing (Quick
/// Settings, the search box), the call does nothing.
/// </summary>
internal interface ITaskbarView
{
    DisplayMonitor Monitor { get; }

    nint Handle { get; }

    /// <summary>Where the taskbar is on screen when shown (an auto-hidden one may be slid away), in pixels.</summary>
    RectInt32 ScreenBounds { get; }

    /// <summary>Shows the new window without taking the focus.</summary>
    void Show();

    /// <summary>Closes the window and its menus and flyouts, giving its screen space back.</summary>
    void Shut();

    /// <summary>Slides an auto-hidden taskbar back into view.</summary>
    void Reveal();

    /// <summary>Rebuilds the task buttons from the pinned apps and the tracked windows.</summary>
    void RefreshTasks();

    /// <summary>The tray's chevron and hidden icons, after the tray's settings changed.</summary>
    void RefreshTray();

    /// <param name="accent">The taskbar's colour when Windows shows the accent colour on it.</param>
    void SetTheme(ElementTheme theme, Color? accent);

    void SetBackdrop(Backdrop kind);

    /// <summary>The clock after <see cref="Taskbar.Taskbars.ClockSettings"/>, the time zone or the formats changed.</summary>
    void UpdateClock();

    /// <summary>While the notification center and calendar are open above this taskbar.</summary>
    void ShowClockOpen(bool open);

    /// <summary>Follows Explorer's search setting.</summary>
    void SetSearchMode(Taskbar.TaskbarSearchMode mode);

    /// <summary>Makes way for a full-screen window on this monitor, or with 0 returns to the topmost band.</summary>
    void SetFullScreenWindow(nint window);

    /// <summary>The button of a window on screen, or null when it has none on this taskbar.</summary>
    RectInt32? TaskButtonBounds(nint hwnd);

    /// <summary>Where a tray icon is on screen (or what stands for it when it's hidden), or null.</summary>
    RectInt32? TrayIconBounds(TrayIcon icon);

    /// <summary>Win+1…9 (0-based here).</summary>
    void ActivateTask(int index);

    /// <summary>Win+Shift+1…9, and Win+Ctrl+Shift+1…9 as administrator.</summary>
    void LaunchTask(int index, bool elevated);

    /// <summary>Win+Ctrl+1…9.</summary>
    void ActivateLastWindow(int index);

    /// <summary>Win+Alt+1…9.</summary>
    void ShowJumpList(int index);

    /// <summary>Win+T (Win+Shift+T from the end).</summary>
    void FocusTaskList(bool last = false);

    /// <summary>Win+B.</summary>
    void FocusTray();

    /// <summary>Win+A, Win+Ctrl+V, Win+K, Win+P.</summary>
    void ShowQuickSettings(QuickSettingsPage page);

    /// <summary>Win+Space as the shell.</summary>
    void RunInputSwitch(InputSwitchCommand command);

    /// <summary>Win+X.</summary>
    void ToggleQuickLinks();
}
