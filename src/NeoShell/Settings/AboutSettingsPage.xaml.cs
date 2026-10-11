using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Taskbar;

namespace NeoShell.Settings;

/// <summary>NeoShell's version and run mode, the way back to Explorer (or out, alongside it) and the docs.</summary>
internal sealed partial class AboutSettingsPage : SettingsPageView
{
    private const string DocumentationUrl = "https://github.com/martinmine/NeoShell/blob/master/docs/design.md";

    public AboutSettingsPage(Taskbars taskbars)
        : base(taskbars)
    {
        InitializeComponent();
    }

    protected override void Load()
    {
        VersionText.Text = SettingsPages.Version(
            typeof(AboutSettingsPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        RunModeText.Text = SettingsPages.RunModeTitle(RunMode);
        RunModeCard.Description = SettingsPages.RunModeDescription(RunMode);
        SwitchCard.Visibility = RunMode == RunMode.Shell ? Visibility.Visible : Visibility.Collapsed;
        ExitCard.Visibility = RunMode == RunMode.Shell ? Visibility.Collapsed : Visibility.Visible;
    }

    // As Start's Switch to Explorer button: asked first, Cancel the default.
    private async void SwitchToExplorer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Switch to Explorer?",
            Content = "NeoShell will close and Explorer will be your shell again, now and the next time you sign in.",
            PrimaryButtonText = "Switch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            Taskbars.SwitchToExplorer();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Taskbars.Exit();

    private void Documentation_Click(object sender, RoutedEventArgs e) =>
        Launcher.Launch(new PinnedApp("NeoShell documentation", Path: DocumentationUrl));
}
