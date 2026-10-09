using NeoShell.Interop.Bluetooth;
using NeoShell.Interop.Display;
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

    /// <summary>
    /// A paired device's line on the Bluetooth page, in Windows' words (DevicesFlow's): what's under way, or how it's
    /// connected (an audio device by its profiles: calls are "mic", stereo sound is "audio").
    /// </summary>
    public static string BluetoothStatus(PairedDevice device, BluetoothActivity activity) => activity switch
    {
        BluetoothActivity.Connecting => "Connecting...",
        BluetoothActivity.Disconnecting => "Disconnecting",
        BluetoothActivity.ConnectFailed => "Couldn’t connect.",
        BluetoothActivity.DisconnectFailed => "Couldn’t disconnect.",
        _ when !device.IsConnected => "Paired",
        _ => device.Audio switch
        {
            BluetoothAudioProfiles.Voice | BluetoothAudioProfiles.Music => "Connected mic, audio",
            BluetoothAudioProfiles.Voice => "Connected mic",
            BluetoothAudioProfiles.Music => "Connected audio",
            _ => "Connected",
        },
    };

    /// <summary>The battery level beside the status, while the device is connected and reports one.</summary>
    public static string? BluetoothBattery(PairedDevice device) =>
        device.IsConnected && device.Battery is int level ? $"{level}%" : null;

    /// <summary>
    /// What choosing a device does, as in Windows: a paired audio device connects straight away, a connected one opens
    /// to offer Disconnect; other devices are only listed (Windows can't connect them).
    /// </summary>
    public static BluetoothChoice BluetoothChoose(PairedDevice device) =>
        !device.IsAudio ? BluetoothChoice.Nothing
        : device.IsConnected ? BluetoothChoice.OfferDisconnect
        : BluetoothChoice.Connect;

    /// <summary>The battery's glyph in tenths, with the plug while charging.</summary>
    public static string BatteryGlyph(BatteryState battery)
    {
        int tenths = Math.Clamp((int)Math.Round(battery.Percent / 10.0), 0, 10);
        return ((char)((battery.IsCharging ? 0xEBAB : 0xEBA0) + tenths)).ToString();
    }

    public static string BatteryToolTip(BatteryState battery) =>
        $"Battery: {battery.Percent}% {(battery.IsCharging ? "charging" : "remaining")}";

    /// <summary>
    /// Rotation lock's tile: only where the screen can turn (an orientation sensor), greyed while the lock can't change
    /// (docked, several screens, a remote session), on while the screen stays put.
    /// </summary>
    public static QuickActionState RotationLockTile(AutoRotation rotation) =>
        rotation.IsSupported ? new QuickActionState(true, rotation.CanChange, rotation.IsLocked) : QuickActionState.Hidden;

    /// <summary>The Nearby sharing page's heading and text, as Windows' (ControlCenter's NearShareL2Page strings).</summary>
    public static (string Title, string Text) NearbySharingText(bool on) => on
        ? ("Nearby sharing is on", "Bluetooth and WLAN have been turned on to help you quickly share files and more.")
        : ("Nearby sharing is off", "Bluetooth and WLAN must be on to use nearby sharing. When you turn on nearby sharing, Bluetooth and WLAN will be turned on automatically.");
}

/// <summary>What's under way for a device on the Bluetooth page, or how it ended.</summary>
public enum BluetoothActivity { None, Connecting, Disconnecting, ConnectFailed, DisconnectFailed }

public enum BluetoothChoice { Nothing, Connect, OfferDisconnect }
