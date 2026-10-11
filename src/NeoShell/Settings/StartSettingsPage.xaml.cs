using System.Globalization;
using Microsoft.UI.Xaml;
using NeoShell.Taskbar;

namespace NeoShell.Settings;

/// <summary>Start: its size, NeoShell's one setting of its own, and Windows' settings that it follows.</summary>
internal sealed partial class StartSettingsPage : SettingsPageView
{
    private static readonly ShellSettings Defaults = new();

    public StartSettingsPage(Taskbars taskbars)
        : base(taskbars)
    {
        InitializeComponent();
    }

    protected override void Load()
    {
        string size = string.Format(CultureInfo.CurrentCulture, "{0:0} × {1:0}", Current.StartMenuWidth, Current.StartMenuHeight);
        SizeCard.Description = $"Start keeps the size you give it by dragging its top corner; now {size}";
        ResetSizeButton.IsEnabled = Current.StartMenuWidth != Defaults.StartMenuWidth || Current.StartMenuHeight != Defaults.StartMenuHeight;
    }

    private void ResetSizeButton_Click(object sender, RoutedEventArgs e)
    {
        Save(Current with { StartMenuWidth = Defaults.StartMenuWidth, StartMenuHeight = Defaults.StartMenuHeight });
        Reload();
    }

    private void WindowsStartSettings_Click(object sender, RoutedEventArgs e) =>
        OpenWindowsSettings("Start settings", "ms-settings:personalization-start");

    private void StartFolders_Click(object sender, RoutedEventArgs e) =>
        OpenWindowsSettings("Start folders", "ms-settings:personalization-start-places");
}
