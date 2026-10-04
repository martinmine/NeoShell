using Windows.Media.Audio;
using Windows.Media.Devices;

namespace NeoShell.Interop.Audio;

public sealed record SpatialFormat(string Subtype, string Name);

/// <summary>Spatial sound on the default output, as Windows' Sound output page offers it (WinRT).</summary>
public static class SpatialSound
{
    private static readonly SpatialFormat s_off = new("{00000000-0000-0000-0000-000000000000}", "Off");

    /// <summary>
    /// What the default output can use: Off, and Windows Sonic for Headphones where it's supported. Dolby Atmos and
    /// DTS report themselves supported too, but only work with their apps installed and licensed, which Windows' own
    /// page checks with them; they aren't offered. Empty without an output.
    /// </summary>
    public static (IReadOnlyList<SpatialFormat> Formats, string Current) ForDefaultOutput()
    {
        if (Configuration() is not { IsSpatialAudioSupported: true } configuration)
            return ([], s_off.Subtype);

        var formats = new List<SpatialFormat> { s_off };
        if (configuration.IsSpatialAudioFormatSupported(SpatialAudioFormatSubtype.WindowsSonic))
            formats.Add(new SpatialFormat(SpatialAudioFormatSubtype.WindowsSonic, "Windows Sonic for Headphones"));
        return (formats, configuration.DefaultSpatialAudioFormat);
    }

    /// <summary>Turns spatial sound off, or on with the format; false if Windows refused.</summary>
    public static async Task<bool> SetAsync(SpatialFormat format)
    {
        if (Configuration() is not { } configuration)
            return false;

        SetDefaultSpatialAudioFormatResult result = await configuration.SetDefaultSpatialAudioFormatAsync(format.Subtype);
        return result.Status == SetDefaultSpatialAudioFormatStatus.Succeeded;
    }

    private static SpatialAudioDeviceConfiguration? Configuration()
    {
        string id = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        return string.IsNullOrEmpty(id) ? null : SpatialAudioDeviceConfiguration.GetForDeviceId(id);
    }
}
