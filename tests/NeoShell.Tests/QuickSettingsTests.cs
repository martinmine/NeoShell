using NeoShell.Interop.Bluetooth;
using NeoShell.Interop.Display;
using NeoShell.Interop.Network;
using NeoShell.Interop.Power;
using NeoShell.QuickSettings;

namespace NeoShell.Tests;

public sealed class QuickSettingsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(10, 2)]
    [InlineData(13, 3)]
    public void Tiles_come_in_pages_of_two_rows_of_three(int tiles, int pages)
    {
        Assert.Equal(pages, QuickSettingsDisplay.PageCount(tiles));
    }

    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(1, 6, 0)]  // tiles went away: the last page left
    [InlineData(-1, 10, 0)]
    [InlineData(5, 10, 1)]
    public void Tile_page_stays_within_the_pages_there_are(int page, int tiles, int expected)
    {
        Assert.Equal(expected, QuickSettingsDisplay.ClampPage(page, tiles));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    [InlineData(2, "")]
    [InlineData(3, "")]
    [InlineData(4, "")]
    public void Wifi_glyph_shows_the_signal(int bars, string expected)
    {
        Assert.Equal(expected, QuickSettingsDisplay.WifiGlyph(bars));
    }

    [Fact]
    public void Wifi_status_says_connected_and_secured()
    {
        Assert.Equal("Connected, secured", QuickSettingsDisplay.WifiStatus(new WifiNetwork("Home", 4, true, true)));
        Assert.Equal("Open", QuickSettingsDisplay.WifiStatus(new WifiNetwork("Cafe", 2, false, false)));
    }

    [Fact]
    public void Wifi_networks_are_listed_once_each_strongest_connected_first()
    {
        WifiNetwork[] seen =
        [
            new("Neighbour", 3, true, false),
            new("Home", 2, true, false),
            new("Home", 4, true, false), // a second access point of the same network
            new("", 4, true, false),     // hidden
            new("Cafe", 3, false, false),
            new("Attic", 1, true, false),
        ];

        IReadOnlyList<WifiNetwork> listed = WifiNetworks.Arrange(seen, connectedSsid: "Attic");

        Assert.Equal(["Attic", "Home", "Cafe", "Neighbour"], listed.Select(network => network.Ssid));
        Assert.True(listed[0].IsConnected);
        Assert.Equal(4, listed[1].SignalBars);
        Assert.All(listed.Skip(1), network => Assert.False(network.IsConnected));
    }

    [Theory]
    [InlineData(BluetoothDeviceKind.Audio, "")]
    [InlineData(BluetoothDeviceKind.Mouse, "")]
    [InlineData(BluetoothDeviceKind.Other, "")]
    public void Bluetooth_glyph_shows_the_kind_of_device(BluetoothDeviceKind kind, string expected)
    {
        Assert.Equal(expected, QuickSettingsDisplay.BluetoothGlyph(kind));
    }

    private static readonly Guid Buds = new("6f1d3c2a-0000-0000-0000-000000000001");
    private static readonly Guid Mouse = new("6f1d3c2a-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(false, BluetoothAudioProfiles.None, "Paired")]
    [InlineData(true, BluetoothAudioProfiles.None, "Connected")]
    [InlineData(true, BluetoothAudioProfiles.Music, "Connected audio")]
    [InlineData(true, BluetoothAudioProfiles.Voice, "Connected mic")]
    [InlineData(true, BluetoothAudioProfiles.Voice | BluetoothAudioProfiles.Music, "Connected mic, audio")]
    public void Bluetooth_status_says_how_a_device_is_connected(bool connected, BluetoothAudioProfiles audio, string expected)
    {
        var device = new PairedDevice("Buds", BluetoothDeviceKind.Audio, connected) { IsAudio = true, Audio = audio };
        Assert.Equal(expected, QuickSettingsDisplay.BluetoothStatus(device, BluetoothActivity.None));
    }

    [Theory]
    [InlineData(BluetoothActivity.Connecting, "Connecting...")]
    [InlineData(BluetoothActivity.Disconnecting, "Disconnecting")]
    [InlineData(BluetoothActivity.ConnectFailed, "Couldn’t connect.")]
    [InlineData(BluetoothActivity.DisconnectFailed, "Couldn’t disconnect.")]
    public void Bluetooth_status_shows_what_is_under_way_or_how_it_ended(BluetoothActivity activity, string expected)
    {
        var device = new PairedDevice("Buds", BluetoothDeviceKind.Audio, true) { IsAudio = true, Audio = BluetoothAudioProfiles.Music };
        Assert.Equal(expected, QuickSettingsDisplay.BluetoothStatus(device, activity));
    }

    [Fact]
    public void Bluetooth_battery_shows_only_while_connected()
    {
        var buds = new PairedDevice("Buds", BluetoothDeviceKind.Audio, true) { Battery = 80 };
        Assert.Equal("80%", QuickSettingsDisplay.BluetoothBattery(buds));
        // Windows keeps the last level after a device goes.
        Assert.Null(QuickSettingsDisplay.BluetoothBattery(buds with { IsConnected = false }));
        Assert.Null(QuickSettingsDisplay.BluetoothBattery(buds with { Battery = null }));
    }

    [Theory]
    [InlineData(0, 0xECB9)]
    [InlineData(4, 0xECB9)]
    [InlineData(5, 0xECBA)]
    [InlineData(40, 0xECBB)]
    [InlineData(41, 0xECBC)]
    [InlineData(76, 0xECBD)]
    [InlineData(94, 0xECBE)]
    [InlineData(95, 0xECBF)]
    [InlineData(100, 0xECBF)]
    public void Bluetooth_battery_glyph_steps_as_Windows(int percent, int glyph)
    {
        Assert.Equal(((char)glyph).ToString(), QuickSettingsDisplay.BluetoothBatteryGlyph(percent));
        Assert.Equal("", QuickSettingsDisplay.BluetoothBatteryGlyph(101));
    }

    [Fact]
    public void Choosing_an_audio_device_connects_it_or_offers_to_disconnect_and_other_devices_only_list()
    {
        var buds = new PairedDevice("Buds", BluetoothDeviceKind.Audio, false) { IsAudio = true };
        Assert.Equal(BluetoothChoice.Connect, QuickSettingsDisplay.BluetoothChoose(buds));
        Assert.Equal(BluetoothChoice.OfferDisconnect, QuickSettingsDisplay.BluetoothChoose(buds with { IsConnected = true }));
        Assert.Equal(BluetoothChoice.Nothing, QuickSettingsDisplay.BluetoothChoose(new PairedDevice("Mouse", BluetoothDeviceKind.Mouse, true)));
        Assert.Equal(BluetoothChoice.Nothing, QuickSettingsDisplay.BluetoothChoose(new PairedDevice("Mouse", BluetoothDeviceKind.Mouse, false)));
    }

    [Fact]
    public void Bluetooth_audio_profiles_come_from_the_connected_endpoints()
    {
        AudioEndpointInfo stereo = new("a", Buds, IsActive: true, IsInput: false, IsHeadset: false);
        AudioEndpointInfo handsFreeOut = new("b", Buds, IsActive: true, IsInput: false, IsHeadset: true);
        AudioEndpointInfo handsFreeIn = new("c", Buds, IsActive: true, IsInput: true, IsHeadset: true);

        Assert.Equal(BluetoothAudioProfiles.Music, BluetoothAudio.Connected([stereo]));
        Assert.Equal(BluetoothAudioProfiles.Voice, BluetoothAudio.Connected([handsFreeOut, handsFreeIn]));
        Assert.Equal(BluetoothAudioProfiles.Voice | BluetoothAudioProfiles.Music, BluetoothAudio.Connected([stereo, handsFreeOut, handsFreeIn]));
        // A disconnected device keeps its endpoints, unplugged.
        Assert.Equal(BluetoothAudioProfiles.None, BluetoothAudio.Connected([stereo with { IsActive = false }]));
    }

    [Fact]
    public void Paired_devices_are_listed_once_each_with_their_audio_and_battery_connected_first()
    {
        PairedDevice[] paired =
        [
            new("Buds", BluetoothDeviceKind.Audio, false) { ContainerId = Buds, Address = 1 },
            // The same buds' LE side, connected.
            new("Buds", BluetoothDeviceKind.Other, true) { ContainerId = Buds, Address = 2, IsLowEnergy = true },
            new("Mouse", BluetoothDeviceKind.Mouse, false) { ContainerId = Mouse },
            new("Keyboard", BluetoothDeviceKind.Keyboard, false),
            new("Pen", BluetoothDeviceKind.Other, false),
        ];
        AudioEndpointInfo[] endpoints = [new("a", Buds, IsActive: true, IsInput: false, IsHeadset: false)];
        var battery = new Dictionary<Guid, int> { [Buds] = 70, [Mouse] = 20 };

        IReadOnlyList<PairedDevice> listed = BluetoothDevices.Combine(paired, endpoints, battery);

        Assert.Equal(["Buds", "Keyboard", "Mouse", "Pen"], listed.Select(device => device.Name));
        PairedDevice buds = listed[0];
        Assert.True(buds.IsConnected);
        Assert.False(buds.IsLowEnergy);
        Assert.Equal(1ul, buds.Address);
        Assert.True(buds.IsAudio);
        Assert.Equal(BluetoothAudioProfiles.Music, buds.Audio);
        Assert.Equal(70, buds.Battery);
        Assert.False(listed[2].IsAudio);
        Assert.Equal(20, listed[2].Battery);
    }

    [Theory]
    [InlineData(0, false, "")]
    [InlineData(54, false, "")]
    [InlineData(100, false, "")]
    [InlineData(100, true, "")]
    public void Battery_glyph_shows_the_charge_in_tenths(int percent, bool charging, string expected)
    {
        Assert.Equal(expected, QuickSettingsDisplay.BatteryGlyph(new BatteryState(percent, charging)));
    }

    [Fact]
    public void Battery_tooltip_says_how_much_is_left()
    {
        Assert.Equal("Battery: 54% remaining", QuickSettingsDisplay.BatteryToolTip(new BatteryState(54, false)));
        Assert.Equal("Battery: 80% charging", QuickSettingsDisplay.BatteryToolTip(new BatteryState(80, true)));
    }

    [Theory]
    [InlineData(0x10, false, false, false)] // AR_NOSENSOR: a desktop or this VM, no tile
    [InlineData(0x20, false, false, false)] // AR_NOT_SUPPORTED
    [InlineData(0x00, true, true, false)]   // free to turn
    [InlineData(0x01, true, true, true)]    // AR_DISABLED: locked
    [InlineData(0x08, true, false, true)]   // AR_MULTIMON: greyed, the screen stays put
    [InlineData(0x40, true, false, true)]   // AR_DOCKED
    [InlineData(0x80, true, false, true)]   // AR_LAPTOP: a convertible used as a laptop
    [InlineData(0x04, true, false, true)]   // AR_REMOTESESSION
    public void Rotation_lock_shows_only_with_a_sensor_and_greys_while_it_cant_change(int state, bool shown, bool available, bool on)
    {
        QuickActionState tile = QuickSettingsDisplay.RotationLockTile(AutoRotation.From(state));

        Assert.Equal(shown, tile.IsShown);
        Assert.Equal(available, tile.IsAvailable);
        Assert.Equal(on, tile.IsOn);
    }

    [Fact]
    public void Vpn_connections_are_the_phonebooks_entries_of_type_two()
    {
        const string phonebook = """
            [Office]
            Encoding=1
            Type=2
            PreferredDevice=WAN Miniport (IKEv2)

            [Dial-up]
            Type=1

            [Broadband]
            Type=5
            [Home lab]
            Type=2
            """;

        Assert.Equal(["Office", "Home lab"], VpnConnections.VpnEntries(phonebook));
        Assert.Equal(["Crlf"], VpnConnections.VpnEntries("[Crlf]\r\nType=2\r\n"));
        Assert.Empty(VpnConnections.VpnEntries(""));
    }

    [Fact]
    public void Nearby_sharing_page_says_whether_it_is_on()
    {
        Assert.Equal("Nearby sharing is on", QuickSettingsDisplay.NearbySharingText(true).Title);
        Assert.StartsWith("Bluetooth and WLAN must be on", QuickSettingsDisplay.NearbySharingText(false).Text);
    }

    private const int Win = 0x5B;
    private const int Control = 0xA2;

    [Fact]
    public void Win_A_opens_Quick_Settings_and_its_letter_is_swallowed_down_and_up()
    {
        var keys = new PanelKeys();

        Assert.False(keys.OnKey(Win, true, out _));
        Assert.True(keys.OnKey('A', true, out PanelShortcut? page));
        Assert.Equal(PanelShortcut.QuickSettings, page);
        Assert.True(keys.OnKey('A', true, out page)); // key repeat: swallowed, not opened again
        Assert.Null(page);
        Assert.True(keys.OnKey('A', false, out _));
        Assert.False(keys.OnKey(Win, false, out _));
    }

    [Theory]
    [InlineData('K', false, PanelShortcut.Cast)]
    [InlineData('P', false, PanelShortcut.Project)]
    [InlineData('V', true, PanelShortcut.SoundOutput)]
    [InlineData('N', false, PanelShortcut.NotificationCenter)]
    [InlineData('X', false, PanelShortcut.QuickLinks)]
    [InlineData('C', false, PanelShortcut.Copilot)]
    public void Win_shortcuts_open_their_panel(char key, bool control, PanelShortcut expected)
    {
        var keys = new PanelKeys();
        keys.OnKey(Win, true, out _);
        if (control)
            keys.OnKey(Control, true, out _);

        Assert.True(keys.OnKey(key, true, out PanelShortcut? page));
        Assert.Equal(expected, page);
    }

    [Fact]
    public void Other_keys_pass_through()
    {
        var keys = new PanelKeys();

        Assert.False(keys.OnKey('A', true, out _));     // typing an A
        keys.OnKey('A', false, out _);
        keys.OnKey(Win, true, out _);
        Assert.False(keys.OnKey('D', true, out _));     // Win+D is a hotkey of its own
        Assert.False(keys.OnKey('V', true, out _));     // Win+V is the clipboard
        keys.OnKey(Control, true, out _);
        Assert.False(keys.OnKey('A', true, out _));     // Win+Ctrl+A isn't Quick Settings
    }

    [Fact]
    public void The_Copilot_key_is_Win_Shift_F23_and_its_F23_is_swallowed()
    {
        const int Shift = 0xA0, F23 = 0x86;
        var keys = new PanelKeys();
        keys.OnKey(Win, true, out _);
        keys.OnKey(Shift, true, out _);

        Assert.True(keys.OnKey(F23, true, out PanelShortcut? shortcut));
        Assert.Equal(PanelShortcut.Copilot, shortcut);
        Assert.True(keys.OnKey(F23, false, out _));
        Assert.False(keys.OnKey(Shift, false, out _));
        Assert.False(keys.OnKey(F23, true, out _)); // F23 without Win and Shift is some other key
    }

    [Theory]
    [InlineData(false, null, null, "App:Microsoft.Copilot_8wekyb3d8bbwe!App")] // never set: the Copilot app
    [InlineData(true, "App", "Microsoft.Paint_8wekyb3d8bbwe!App", "App:Microsoft.Paint_8wekyb3d8bbwe!App")]
    [InlineData(true, "AppEnforcedByPolicy", "Contoso.App_abc!App", "App:Contoso.App_abc!App")]
    [InlineData(true, "App", "", "Unset")]
    [InlineData(true, "Search", "Microsoft.Paint_8wekyb3d8bbwe!App", "Search")]
    [InlineData(true, null, null, "Unset")]
    [InlineData(true, "Copilot", null, "Unset")]
    public void Copilot_key_target_follows_Explorers_reading_of_BrandedKey(bool keyExists, string? choice, string? aumid, string expected)
    {
        string actual = CopilotKey.Choose(keyExists, choice, aumid) switch
        {
            CopilotKeyTarget.App app => "App:" + app.AppUserModelId,
            CopilotKeyTarget.Search => "Search",
            _ => "Unset",
        };
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0xA4, 'K')] // Win+Alt+K mutes the microphone
    [InlineData(0xA0, 'N')] // Win+Shift+N isn't the notification center
    public void Shortcuts_with_alt_or_shift_are_left_to_their_hotkeys(int modifier, char key)
    {
        var keys = new PanelKeys();
        keys.OnKey(Win, true, out _);
        keys.OnKey(modifier, true, out _);

        Assert.False(keys.OnKey(key, true, out PanelShortcut? page));
        Assert.Null(page);
    }
}
