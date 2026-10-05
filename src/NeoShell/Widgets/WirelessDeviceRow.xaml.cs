using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Interop.Wireless;

namespace NeoShell.Widgets;

internal sealed partial class WirelessDeviceRow : UserControl
{
    /// <summary>At or below this, and not charging, the bar shows the battery is low (WirelessStatus' default).</summary>
    public const int LowLevel = 10;

    public WirelessDeviceRow() => InitializeComponent();

    public void Show(WirelessDevice device)
    {
        bool available = device.IsConnected && device.Level is not null;
        bool charging = available && device.IsCharging == true;
        KindIcon.Glyph = Glyph(device.Kind);
        NameText.Text = device.Name;
        Bar.Value = device.Level ?? 0;
        Bar.ShowError = available && !charging && device.Level <= LowLevel;
        Bar.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        ChargingIcon.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        LevelText.Text = available ? $"{device.Level}%" : "Unavailable";
        // Out of reach or switched off: dimmed, as in WirelessStatus.
        Root.Opacity = available ? 1 : 0.5;
        AutomationProperties.SetName(this, $"{device.Name}, {LevelText.Text}{(charging ? ", charging" : "")}");
    }

    /// <summary>Segoe Fluent Icons glyph for the kind of device.</summary>
    private static string Glyph(WirelessDeviceKind kind) => kind switch
    {
        WirelessDeviceKind.Headset => "\uE7F6",
        WirelessDeviceKind.Mouse => "\uE962",
        WirelessDeviceKind.Keyboard => "\uE765",
        WirelessDeviceKind.Controller => "\uE7FC",
        WirelessDeviceKind.Bluetooth => "\uE702",
        _ => "\uE772",
    };
}
