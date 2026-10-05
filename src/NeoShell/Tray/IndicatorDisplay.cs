using NeoShell.Interop.Network;

namespace NeoShell.Tray;

/// <summary>Glyphs (Segoe Fluent Icons) and tooltips of the network, volume and microphone indicators.</summary>
public static class IndicatorDisplay
{
    public const string MicrophoneGlyph = "";

    public const string MicrophoneMutedGlyph = "";

    public const string AirplaneGlyph ="";

    /// <summary>The connection's glyph; in airplane mode the plane, whatever is still connected (Ethernet).</summary>
    public static string NetworkGlyph(NetworkState state, bool airplaneMode = false) => airplaneMode ? AirplaneGlyph : state.Kind switch
    {
        NetworkKind.WiFi when !state.HasInternet => "",   // Wi-Fi with a warning
        NetworkKind.WiFi => state.SignalBars switch
        {
            <= 1 => "",
            2 => "",
            3 => "",
            _ => "",
        },
        NetworkKind.Ethernet => state.HasInternet ? "" : "",
        NetworkKind.Cellular => "",
        NetworkKind.Other when state.HasInternet => "",   // globe
        _ => "",                                           // globe, blocked: no network or no internet
    };

    public static string NetworkToolTip(NetworkState state, bool airplaneMode = false)
    {
        if (airplaneMode)
            return "Airplane mode";
        if (state.Kind == NetworkKind.Disconnected)
            return "Not connected";

        string name = state.Name ?? state.Kind switch
        {
            NetworkKind.WiFi => "Wi-Fi",
            NetworkKind.Ethernet => "Ethernet",
            NetworkKind.Cellular => "Cellular",
            _ => "Network",
        };
        return $"{name}\n{(state.HasInternet ? "Internet access" : "No internet access")}";
    }

    /// <param name="volume">0 to 1.</param>
    public static string VolumeGlyph(bool hasDevice, float volume, bool muted) =>
        !hasDevice || muted ? ""
        : volume <= 0 ? ""
        : volume < 1 / 3f ? ""
        : volume < 2 / 3f ? ""
        : "";

    public static string VolumeToolTip(string? deviceName, float volume, bool muted) =>
        deviceName is null ? "No audio output device" : $"{deviceName}: {(muted ? "muted" : $"{Percent(volume)}%")}";

    public static int Percent(float volume) => (int)Math.Round(volume * 100);

    /// <summary>A mouse wheel notch is 2%, as on the Windows 11 taskbar.</summary>
    public static float WheelVolume(float volume, int wheelDelta) => Math.Clamp(volume + wheelDelta / 120f * 0.02f, 0, 1);

    /// <param name="muted">The microphone is muted (Win+Alt+K): said first.</param>
    public static string MicrophoneToolTip(IReadOnlyList<string> apps, bool muted = false) => (muted ? "Microphone muted\n" : "") + apps.Count switch
    {
        0 => "",
        1 => $"{apps[0]} is using your microphone",
        _ => $"{apps.Count} apps are using your microphone:\n{string.Join("\n", apps)}",
    };
}
