using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Audio;

/// <summary>An app's sound on the default output: all its sessions, which the mixer sets together.</summary>
public sealed unsafe class AudioApp
{
    private readonly List<ISimpleAudioVolume> _sessions = [];

    internal AudioApp(string key, string? name, bool isSystemSounds, string? processPath, string? packageAppId)
    {
        Key = key;
        Name = name;
        IsSystemSounds = isSystemSounds;
        ProcessPath = processPath;
        PackageAppId = packageAppId;
    }

    /// <summary>Stable identity: the packaged app, the executable, or the system sounds.</summary>
    public string Key { get; }

    /// <summary>The name the app gave its sessions, if any; otherwise it's named after its process.</summary>
    public string? Name { get; }

    public bool IsSystemSounds { get; }

    public string? ProcessPath { get; }

    public string? PackageAppId { get; }

    /// <summary>From 0 to 1: the loudest session's, as the mixer has one slider for them all.</summary>
    public float Volume
    {
        get => _sessions.Select(v => v.GetMasterVolume(out float level) == 0 ? level : 0).DefaultIfEmpty(0).Max();
        set
        {
            Guid context = AudioMixer.OwnChanges;
            foreach (ISimpleAudioVolume session in _sessions)
                session.SetMasterVolume(Math.Clamp(value, 0, 1), &context);
        }
    }

    public bool IsMuted
    {
        get => _sessions.All(v => v.GetMute(out int muted) == 0 && muted != 0);
        set
        {
            Guid context = AudioMixer.OwnChanges;
            foreach (ISimpleAudioVolume session in _sessions)
                session.SetMute(value ? 1 : 0, &context);
        }
    }

    internal void Add(ISimpleAudioVolume session) => _sessions.Add(session);
}

/// <summary>
/// The Volume Mixer: each app's volume and mute on the default output device, as Windows' mixer shows them. Use it
/// on the thread that created it; <see cref="Changed"/> comes from Core Audio's threads.
/// </summary>
public sealed unsafe class AudioMixer : IDisposable
{
    /// <summary>Marks the volume changes made here, so they don't come back as news.</summary>
    internal static readonly Guid OwnChanges = Guid.NewGuid();

    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceNotifications _deviceNotifications;
    private readonly SessionNotifications _sessionNotifications;
    private readonly List<IAudioSessionControl2> _sessions = [];
    private IAudioSessionManager2? _manager;
    private volatile bool _stale = true;

    public AudioMixer()
    {
        _enumerator = Ole32.Create<IMMDeviceEnumerator>(CoreAudio.CLSID_MMDeviceEnumerator, CoreAudio.CLSCTX_ALL);
        _sessionNotifications = new SessionNotifications(() =>
        {
            _stale = true;
            Changed?.Invoke();
        }, OwnChanges);
        _deviceNotifications = new DeviceNotifications(flow =>
        {
            if (flow is null or EDataFlow.Render)
            {
                _stale = true;
                Changed?.Invoke();
            }
        });
        _enumerator.RegisterEndpointNotificationCallback(_deviceNotifications);
    }

    /// <summary>
    /// An app started or stopped playing, or its volume was changed elsewhere, or the default output changed. Raised
    /// on a Core Audio thread.
    /// </summary>
    public event Action? Changed;

    /// <summary>The apps with sound on the default output (sessions that haven't expired), system sounds first.</summary>
    public IReadOnlyList<AudioApp> Apps()
    {
        if (_stale)
        {
            _stale = false;
            Unwatch();
            Watch();
        }

        var apps = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        foreach (IAudioSessionControl2 session in _sessions)
        {
            if (session.GetState(out int state) != 0 || state == CoreAudio.AudioSessionStateExpired
                || session.GetProcessId(out uint processId) != 0
                || (object)session is not ISimpleAudioVolume volume)
            {
                continue;
            }

            bool system = session.IsSystemSoundsSession() == CoreAudio.S_OK;
            (string? path, string? packageAppId) = system ? (null, null) : WindowInfo.ReadProcess((int)processId);
            string key = system ? "system" : packageAppId ?? path ?? $"process:{processId}";
            if (!apps.TryGetValue(key, out AudioApp? app))
                apps[key] = app = new AudioApp(key, DisplayName(session), system, path, packageAppId);
            app.Add(volume);
        }
        return [.. apps.Values.OrderByDescending(app => app.IsSystemSounds)];
    }

    public void Dispose()
    {
        Unwatch();
        _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications);
    }

    private void Watch()
    {
        // Fails when there is no output device.
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out IMMDevice device) != 0
            || device.Activate(typeof(IAudioSessionManager2).GUID, CoreAudio.CLSCTX_ALL, 0, out nint pointer) != 0)
        {
            return;
        }

        _manager = ComPointer.TakeOwnership<IAudioSessionManager2>(pointer);
        // The manager reports new sessions only once its session list has been asked for.
        if (_manager.GetSessionEnumerator(out IAudioSessionEnumerator enumerator) == 0 && enumerator.GetCount(out int count) == 0)
        {
            for (int i = 0; i < count; i++)
            {
                if (enumerator.GetSession(i, out IAudioSessionControl2 session) != 0)
                    continue;
                session.RegisterAudioSessionNotification(_sessionNotifications);
                _sessions.Add(session);
            }
        }
        _manager.RegisterSessionNotification(_sessionNotifications);
    }

    private void Unwatch()
    {
        foreach (IAudioSessionControl2 session in _sessions)
            session.UnregisterAudioSessionNotification(_sessionNotifications);
        _manager?.UnregisterSessionNotification(_sessionNotifications);
        _sessions.Clear();
        _manager = null;
    }

    private static string? DisplayName(IAudioSessionControl2 session)
    {
        if (session.GetDisplayName(out nint pointer) != 0 || pointer == 0)
            return null;

        string? name;
        try
        {
            name = Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (!name.StartsWith('@'))
            return name;

        // A resource reference, such as the system sounds' "@%SystemRoot%\System32\AudioSrv.Dll,-202".
        char* buffer = stackalloc char[512];
        return Shlwapi.SHLoadIndirectString(Environment.ExpandEnvironmentVariables(name), buffer, 512, 0) == 0 ? new string(buffer) : null;
    }
}
