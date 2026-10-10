using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Interop.Wireless;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// The wireless devices with a battery and their levels: Bluetooth devices, and the 2.4 GHz devices Windows doesn't
/// know the battery of (Razer mice, the Audeze Maxwell). The same devices the WirelessStatus app lists.
/// </summary>
internal sealed partial class WirelessWidget : WidgetView
{
    private readonly WirelessMonitor _monitor;

    public WirelessWidget(WidgetSettings settings, WirelessMonitor monitor)
    {
        Settings = settings;
        _monitor = monitor;
        InitializeComponent();
        _monitor.Interval = TimeSpan.FromMinutes(PollMinutes);
        _monitor.Updated += Refresh;
        Refresh();
    }

    private int PollMinutes => Math.Clamp(Settings.PollMinutes ?? 5, 1, 60);

    public override bool ContentUnderButtons => true;

    public override void Close() => _monitor.Updated -= Refresh;

    public override FrameworkElement CreateSettings()
    {
        var minutes = new NumberBox
        {
            Header = "Check batteries every (minutes)",
            Minimum = 1,
            Maximum = 60,
            Value = PollMinutes,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        minutes.ValueChanged += (_, _) =>
        {
            if (double.IsNaN(minutes.Value))
                return;
            SaveSettings(Settings with { PollMinutes = (int)minutes.Value });
            _monitor.Interval = TimeSpan.FromMinutes(PollMinutes);
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(minutes);
        panel.Children.Add(new TextBlock
        {
            Text = "Also checked when a device is plugged in or removed.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        return panel;
    }

    private void Refresh()
    {
        if (_monitor.Devices is not { } devices)
            return;

        // Rows are reused: the list rarely changes.
        while (Rows.Children.Count > devices.Count)
            Rows.Children.RemoveAt(Rows.Children.Count - 1);
        while (Rows.Children.Count < devices.Count)
            Rows.Children.Add(new WirelessDeviceRow());
        for (int i = 0; i < devices.Count; i++)
            ((WirelessDeviceRow)Rows.Children[i]).Show(devices[i]);

        StatusText.Text = "No wireless devices with a battery.";
        StatusText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
