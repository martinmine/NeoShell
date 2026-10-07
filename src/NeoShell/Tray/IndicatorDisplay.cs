using NeoShell.Interop.Input;
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

    public const string InputSwitchHint = "To switch input methods, press Windows key + space.";

    // The glyphs (Segoe Fluent Icons) Explorer shows for text services instead of their language's letters, by
    // profile: found as a table of profiles and code points in windowsudk.shellcommon.dll; only the Japanese IME's
    // was seen on the taskbar, the others are paired by what the glyph says.
    private static readonly Dictionary<Guid, string> s_textServiceGlyphs = new()
    {
        [new Guid("fa550b04-5ad7-411f-a5ac-ca038ec515d7")] = "", // Microsoft Pinyin: 拼
        [new Guid("82590c13-f4dd-44f4-ba1d-8667246fdf8e")] = "", // Microsoft Wubi: 五
        [new Guid("d38eff65-aa46-4fd5-91a7-67845fb02f5b")] = "", // Chinese Traditional Array: 行
        [new Guid("b2f9c502-1742-11d4-9790-0080c882687e")] = "", // Microsoft Bopomofo: ㄅ
        [new Guid("4bdf9f03-c7d3-11d4-b2ab-0080c882687e")] = "", // Microsoft ChangJie: 倉
        [new Guid("037b2c25-480c-4d7f-b027-d6ca6b69788a")] = "", // Chinese Traditional DaYi: 易
        [new Guid("6024b45f-5c54-11d4-b921-0080c882687e")] = "", // Microsoft Quick: 速
        [new Guid("a76c93d9-5523-4e90-aafa-4db112f9ac76")] = "", // Microsoft IME (Japanese): Ⓙ
        [new Guid("b5fe1f02-d5f2-4445-9c03-c568f23c99a1")] = "", // Microsoft IME (Korean): 한
        [new Guid("b60af051-257a-46bc-b9d3-84dad819bafb")] = "", // Microsoft Old Hangul: 옛
    };

    /// <summary>The glyph that stands for a text service, or null to show its language's letters.</summary>
    public static string? InputMethodGlyph(InputMethod method) =>
        method.IsTextService && s_textServiceGlyphs.TryGetValue(method.TextServiceProfile, out string? glyph) ? glyph : null;

    /// <summary>The enabled input method that's in front: the same keyboard layout, or the same language's text service.</summary>
    public static InputMethod? MatchInputMethod(IReadOnlyList<InputMethod> methods, CurrentInputMethod current)
    {
        if (!current.IsTextService)
            return methods.FirstOrDefault(m => m.Hkl == current.Hkl);

        bool SameLanguage(InputMethod m) =>
            string.Equals(m.LanguageTag, current.LanguageTag, StringComparison.OrdinalIgnoreCase)
            || m.LanguageTag.StartsWith(current.LanguageTag + "-", StringComparison.OrdinalIgnoreCase);
        return methods.FirstOrDefault(m => m.IsTextService && SameLanguage(m) && m.Keyboard == current.Keyboard)
            ?? methods.FirstOrDefault(m => m.IsTextService && SameLanguage(m));
    }

    /// <summary>
    /// What the input indicator shows: a text service's glyph, or the language's letters over the keyboard's ("ENG",
    /// "NO"); a text service without a glyph shows the letters Windows gives it alone.
    /// </summary>
    public static (string? Glyph, string Language, string Keyboard) InputLabel(CurrentInputMethod current, InputMethod? method) =>
        method is not null && InputMethodGlyph(method) is { } glyph
            ? (glyph, "", "")
            : (null, current.LanguageCode, current.IsTextService ? "" : current.KeyboardCode);

    /// <summary>"English (United Kingdom)", "Norwegian", and how to switch, as Explorer's tooltip.</summary>
    public static string InputToolTip(CurrentInputMethod current, InputMethod? method) =>
        $"{current.Language}\n{method?.Keyboard ?? current.Keyboard}\n\n{InputSwitchHint}";

    /// <summary>Win+Space: the input method after the one in front (before it with <paramref name="backwards"/>), round the list.</summary>
    public static int NextInputMethod(int count, int current, bool backwards) =>
        current < 0 ? (backwards ? count - 1 : 0) : (current + (backwards ? count - 1 : 1)) % count;
}
