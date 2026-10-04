using NeoShell.Interop.Bluetooth;
using NeoShell.Interop.Network;
using NeoShell.Interop.Power;

namespace NeoShell.QuickSettings;

/// <summary>Glyphs (Segoe Fluent Icons), texts and paging of Quick Settings.</summary>
public static class QuickSettingsDisplay
{
    /// <summary>Tiles on a page: two rows of three, as Windows 11 shows them.</summary>
    public const int TilesPerPage = 6;

    public static int PageCount(int tiles) => Math.Max(1, (tiles + TilesPerPage - 1) / TilesPerPage);

    /// <summary>The page to show after the tiles changed: the same one, or the last if it's gone.</summary>
    public static int ClampPage(int page, int tiles) => Math.Clamp(page, 0, PageCount(tiles) - 1);

    /// <summary>Wi-Fi signal in 0 to 4 bars.</summary>
    public static string WifiGlyph(int bars) => bars switch
    {
        <= 1 => "",
        2 => "",
        3 => "",
        _ => "",
    };

    public static string WifiStatus(WifiNetwork network) => (network.IsConnected, network.IsSecured) switch
    {
        (true, true) => "Connected, secured",
        (true, false) => "Connected, open",
        (false, true) => "Secured",
        (false, false) => "Open",
    };

    public static string BluetoothGlyph(BluetoothDeviceKind kind) => kind switch
    {
        BluetoothDeviceKind.Audio => "",     // headphones
        BluetoothDeviceKind.Keyboard => "",
        BluetoothDeviceKind.Mouse => "",
        BluetoothDeviceKind.Phone => "",
        BluetoothDeviceKind.Computer => "",
        BluetoothDeviceKind.Gamepad => "",
        _ => "",                             // Bluetooth
    };

    /// <summary>The battery's glyph in tenths, with the plug while charging.</summary>
    public static string BatteryGlyph(BatteryState battery)
    {
        int tenths = Math.Clamp((int)Math.Round(battery.Percent / 10.0), 0, 10);
        return ((char)((battery.IsCharging ? 0xEBAB : 0xEBA0) + tenths)).ToString();
    }

    public static string BatteryToolTip(BatteryState battery) =>
        $"Battery: {battery.Percent}% {(battery.IsCharging ? "charging" : "remaining")}";
}
