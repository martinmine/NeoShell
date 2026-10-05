using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Audio;

/// <summary>
/// The default output device's (or microphone's) volume and mute, following the user from device to device. Use it on
/// the thread that created it; <see cref="Changed"/> comes from Core Audio's threads, after which the properties read
/// fresh values.
/// </summary>
public sealed unsafe class AudioEndpoint : IDisposable
{
    private readonly EDataFlow _flow;
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceNotifications _deviceNotifications;
    private readonly VolumeNotifications _volumeNotifications;
    private IAudioEndpointVolume? _volume;
    private string? _deviceName;
    private volatile bool _stale = true;

    /// <param name="microphone">The default recording device rather than the default output.</param>
    public AudioEndpoint(bool microphone = false)
    {
        _flow = microphone ? EDataFlow.Capture : EDataFlow.Render;
        _enumerator = Ole32.Create<IMMDeviceEnumerator>(CoreAudio.CLSID_MMDeviceEnumerator, CoreAudio.CLSCTX_ALL);
        _volumeNotifications = new VolumeNotifications(() => Changed?.Invoke());
        _deviceNotifications = new DeviceNotifications(flow =>
        {
            if (flow is null || flow == _flow)
            {
                _stale = true;
                Changed?.Invoke();
            }
        });
        _enumerator.RegisterEndpointNotificationCallback(_deviceNotifications);
    }

    /// <summary>Volume, mute or the default device changed. Raised on a Core Audio thread.</summary>
    public event Action? Changed;

    /// <summary>False when there is no such device at all.</summary>
    public bool HasDevice => Current is not null;

    public string? DeviceName => Current is null ? null : _deviceName;

    /// <summary>Master volume from 0 to 1.</summary>
    public float Volume
    {
        get => Current?.GetMasterVolumeLevelScalar(out float level) == 0 ? level : 0;
        set => Current?.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), 0);
    }

    public bool IsMuted
    {
        get => Current?.GetMute(out int muted) == 0 && muted != 0;
        set => Current?.SetMute(value ? 1 : 0, 0);
    }

    public void Dispose()
    {
        Unbind();
        _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications);
    }

    // Rebinds to the default device after it changed (or on first use).
    private IAudioEndpointVolume? Current
    {
        get
        {
            if (_stale)
            {
                _stale = false;
                Unbind();
                Bind();
            }
            return _volume;
        }
    }

    private void Bind()
    {
        // Fails when there is no such device.
        if (_enumerator.GetDefaultAudioEndpoint(_flow, ERole.Console, out IMMDevice device) != 0)
            return;

        _deviceName = ReadFriendlyName(device);
        if (device.Activate(typeof(IAudioEndpointVolume).GUID, CoreAudio.CLSCTX_ALL, 0, out nint volume) == 0)
        {
            _volume = ComPointer.TakeOwnership<IAudioEndpointVolume>(volume);
            _volume.RegisterControlChangeNotify(_volumeNotifications);
        }
    }

    private void Unbind()
    {
        _volume?.UnregisterControlChangeNotify(_volumeNotifications);
        _volume = null;
        _deviceName = null;
    }

    internal static string? ReadFriendlyName(IMMDevice device)
    {
        const uint STGM_READ = 0;
        if (device.OpenPropertyStore(STGM_READ, out IPropertyStore store) != 0)
            return null;

        Ole32.PROPERTYKEY key = CoreAudio.FriendlyNameKey;
        Ole32.PROPVARIANT value = default;
        try
        {
            return store.GetValue(&key, &value) == 0 && value.vt == Ole32.VT_LPWSTR && value.pointer != 0
                ? new string((char*)value.pointer)
                : null;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }
}
