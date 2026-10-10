using System.Globalization;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>Windows' flag beside which Windows this is: edition, version, build, architecture and install date.</summary>
internal sealed partial class WindowsWidget : WidgetView
{
    public WindowsWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();

        // Read once: the build only changes with a restart.
        WindowsVersion windows = WindowsVersion.Read();
        NameText.Text = windows.Name;
        VersionText.Text = windows.Version.Length > 0 ? $"Version {windows.Version}" : "";
        BuildText.Text = $"OS build {windows.Build}";
        ArchitectureText.Text = windows.Architecture;
        InstalledText.Text = windows.Installed is { } installed
            ? "Installed " + installed.ToString("d", CultureInfo.CurrentCulture)
            : "";
    }
}
