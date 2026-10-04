using NeoShell.Interop.Bluetooth;
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

    private const int Win = 0x5B;
    private const int Control = 0xA2;

    [Fact]
    public void Win_A_opens_Quick_Settings_and_its_letter_is_swallowed_down_and_up()
    {
        var keys = new QuickSettingsKeys();

        Assert.False(keys.OnKey(Win, true, out _));
        Assert.True(keys.OnKey('A', true, out QuickSettingsPage? page));
        Assert.Equal(QuickSettingsPage.Main, page);
        Assert.True(keys.OnKey('A', true, out page)); // key repeat: swallowed, not opened again
        Assert.Null(page);
        Assert.True(keys.OnKey('A', false, out _));
        Assert.False(keys.OnKey(Win, false, out _));
    }

    [Theory]
    [InlineData('K', false, QuickSettingsPage.Cast)]
    [InlineData('P', false, QuickSettingsPage.Project)]
    [InlineData('V', true, QuickSettingsPage.SoundOutput)]
    public void Win_shortcuts_open_their_page(char key, bool control, QuickSettingsPage expected)
    {
        var keys = new QuickSettingsKeys();
        keys.OnKey(Win, true, out _);
        if (control)
            keys.OnKey(Control, true, out _);

        Assert.True(keys.OnKey(key, true, out QuickSettingsPage? page));
        Assert.Equal(expected, page);
    }

    [Fact]
    public void Other_keys_pass_through()
    {
        var keys = new QuickSettingsKeys();

        Assert.False(keys.OnKey('A', true, out _));     // typing an A
        keys.OnKey('A', false, out _);
        keys.OnKey(Win, true, out _);
        Assert.False(keys.OnKey('D', true, out _));     // Win+D is a hotkey of its own
        Assert.False(keys.OnKey('V', true, out _));     // Win+V is the clipboard
        keys.OnKey(Control, true, out _);
        Assert.False(keys.OnKey('A', true, out _));     // Win+Ctrl+A isn't Quick Settings
    }
}
