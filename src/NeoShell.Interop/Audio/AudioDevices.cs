using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Audio;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>The sound outputs (speakers, headphones…), and choosing which one Windows plays through.</summary>
public static class AudioDevices
{
    /// <summary>The enabled, plugged-in outputs, the default one marked; empty without the audio service.</summary>
    public static IReadOnlyList<AudioDevice> Outputs()
    {
        var enumerator = Ole32.Create<IMMDeviceEnumerator>(CoreAudio.CLSID_MMDeviceEnumerator, CoreAudio.CLSCTX_ALL);
        string? defaultId = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out IMMDevice defaultDevice) == 0
            ? Id(defaultDevice)
            : null;

        var outputs = new List<AudioDevice>();
        if (enumerator.EnumAudioEndpoints(EDataFlow.Render, CoreAudio.DEVICE_STATE_ACTIVE, out IMMDeviceCollection devices) != 0
            || devices.GetCount(out uint count) != 0)
        {
            return outputs;
        }

        for (uint i = 0; i < count; i++)
        {
            if (devices.Item(i, out IMMDevice device) == 0 && Id(device) is { } id)
                outputs.Add(new AudioDevice(id, AudioEndpoint.ReadFriendlyName(device) ?? id, id == defaultId));
        }
        return outputs;
    }

    /// <summary>
    /// Makes the output the default for everything, as the Sound control panel's "Set Default" and Windows' own
    /// output picker do: for games and system sounds, media, and calls alike. Throws on failure.
    /// </summary>
    public static void SetDefault(string deviceId)
    {
        var policy = Ole32.Create<IPolicyConfig>(CoreAudio.CLSID_PolicyConfigClient, CoreAudio.CLSCTX_ALL);
        foreach (ERole role in (ERole[])[ERole.Console, ERole.Multimedia, ERole.Communications])
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
    }

    private static string? Id(IMMDevice device)
    {
        if (device.GetId(out nint id) != 0)
            return null;

        try
        {
            return Marshal.PtrToStringUni(id);
        }
        finally
        {
            Marshal.FreeCoTaskMem(id);
        }
    }
}
