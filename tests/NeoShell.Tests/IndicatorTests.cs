using NeoShell.Interop.Network;
using NeoShell.Tray;

namespace NeoShell.Tests;

public sealed class IndicatorTests
{
    [Theory]
    [InlineData(NetworkKind.WiFi, true, 0, "")]
    [InlineData(NetworkKind.WiFi, true, 1, "")]
    [InlineData(NetworkKind.WiFi, true, 2, "")]
    [InlineData(NetworkKind.WiFi, true, 3, "")]
    [InlineData(NetworkKind.WiFi, true, 5, "")]
    [InlineData(NetworkKind.WiFi, false, 5, "")]
    [InlineData(NetworkKind.Ethernet, true, 0, "")]
    [InlineData(NetworkKind.Ethernet, false, 0, "")]
    [InlineData(NetworkKind.Cellular, true, 4, "")]
    [InlineData(NetworkKind.Other, true, 0, "")]
    [InlineData(NetworkKind.Other, false, 0, "")]
    [InlineData(NetworkKind.Disconnected, false, 0, "")]
    public void Network_glyph_shows_the_connection_its_signal_and_missing_internet(NetworkKind kind, bool internet, int bars, string expected)
    {
        Assert.Equal(expected, IndicatorDisplay.NetworkGlyph(new NetworkState(kind, "net", internet, bars)));
    }

    [Fact]
    public void Network_tooltip_names_the_network_and_its_access()
    {
        Assert.Equal("Home\nInternet access", IndicatorDisplay.NetworkToolTip(new NetworkState(NetworkKind.WiFi, "Home", true, 4)));
        Assert.Equal("Ethernet\nNo internet access", IndicatorDisplay.NetworkToolTip(new NetworkState(NetworkKind.Ethernet, null, false, 0)));
        Assert.Equal("Not connected", IndicatorDisplay.NetworkToolTip(NetworkState.Disconnected));
    }

    [Theory]
    [InlineData(true, 0.5f, true, "")]
    [InlineData(false, 0.5f, false, "")]
    [InlineData(true, 0f, false, "")]
    [InlineData(true, 0.2f, false, "")]
    [InlineData(true, 0.5f, false, "")]
    [InlineData(true, 0.9f, false, "")]
    public void Volume_glyph_follows_mute_and_level(bool hasDevice, float volume, bool muted, string expected)
    {
        Assert.Equal(expected, IndicatorDisplay.VolumeGlyph(hasDevice, volume, muted));
    }

    [Fact]
    public void Volume_tooltip_shows_the_device_and_level()
    {
        Assert.Equal("Speakers: 67%", IndicatorDisplay.VolumeToolTip("Speakers", 0.666f, false));
        Assert.Equal("Speakers: muted", IndicatorDisplay.VolumeToolTip("Speakers", 0.666f, true));
        Assert.Equal("No audio output device", IndicatorDisplay.VolumeToolTip(null, 0, false));
    }

    [Theory]
    [InlineData(0.50f, 120, 0.52f)]
    [InlineData(0.50f, -240, 0.46f)]
    [InlineData(0.99f, 120, 1.00f)]
    [InlineData(0.01f, -120, 0.00f)]
    public void Mouse_wheel_changes_volume_two_percent_a_notch(float volume, int delta, float expected)
    {
        Assert.Equal(expected, IndicatorDisplay.WheelVolume(volume, delta), precision: 4);
    }

    [Fact]
    public void Microphone_tooltip_lists_the_apps()
    {
        Assert.Equal("Sound Recorder is using your microphone", IndicatorDisplay.MicrophoneToolTip(["Sound Recorder"]));
        Assert.Equal("2 apps are using your microphone:\nTeams\nOBS", IndicatorDisplay.MicrophoneToolTip(["Teams", "OBS"]));
    }
}
