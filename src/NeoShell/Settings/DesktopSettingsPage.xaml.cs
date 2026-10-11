using Microsoft.UI.Xaml;
using NeoShell.Taskbar;

namespace NeoShell.Settings;

/// <summary>
/// The desktop and wallpaper. They're Windows' settings, which NeoShell's desktop follows as the shell; how icons are
/// shown and sorted is on the desktop's own menu, as in Explorer.
/// </summary>
internal sealed partial class DesktopSettingsPage : SettingsPageView
{
    public DesktopSettingsPage(Taskbars taskbars)
        : base(taskbars)
    {
        InitializeComponent();
    }

    protected override void Load()
    {
        DesktopNote.Text = RunMode == RunMode.Shell
            ? "NeoShell shows the wallpaper and the desktop icons on every display, from Windows' settings. View and Sort by are on the desktop's right-click menu, as in Explorer."
            : "Explorer shows the desktop while it's the shell; NeoShell's desktop follows the same settings when NeoShell is the shell.";
        // Settings' Background page can't open without Explorer (see Launcher.OpenSettings), and Control Panel's sends there.
        BackgroundLink.Visibility = RunMode == RunMode.Shell ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Background_Click(object sender, RoutedEventArgs e) =>
        OpenWindowsSettings("Background", "ms-settings:personalization-background");

    // The classic dialog, which also opens as the shell (Personalize on the desktop's menu opens it then).
    private void DesktopIcons_Click(object sender, RoutedEventArgs e) =>
        Launcher.Launch(new PinnedApp("Desktop icon settings", Path: "rundll32.exe", Arguments: "shell32.dll,Control_RunDLL desk.cpl,,0"));
}
