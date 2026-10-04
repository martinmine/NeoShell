using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Audio;

/// <summary>
/// Which processes are recording from a microphone right now: those with an active audio session on any capture
/// device. Use it on the thread that created it; <see cref="Changed"/> comes from Core Audio's threads.
/// </summary>
public sealed class CaptureMonitor : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceNotifications _deviceNotifications;
    private readonly SessionNotifications _sessionNotifications;
    private readonly List<IAudioSessionManager2> _managers = [];
    private readonly List<IAudioSessionControl2> _sessions = [];
    private volatile bool _stale = true;

    public CaptureMonitor()
    {
        _enumerator = Ole32.Create<IMMDeviceEnumerator>(CoreAudio.CLSID_MMDeviceEnumerator, CoreAudio.CLSCTX_ALL);
        // New sessions and devices are only noted here; they're watched from the next ActiveProcessIds.
        _sessionNotifications = new SessionNotifications(() =>
        {
            _stale = true;
            Changed?.Invoke();
        });
        _deviceNotifications = new DeviceNotifications(flow =>
        {
            if (flow is null or EDataFlow.Capture)
            {
                _stale = true;
                Changed?.Invoke();
            }
        });
        _enumerator.RegisterEndpointNotificationCallback(_deviceNotifications);
    }

    /// <summary>A capture session started, stopped, appeared or went away. Raised on a Core Audio thread.</summary>
    public event Action? Changed;

    /// <summary>The processes with an active capture session, other than the system sounds session.</summary>
    public IReadOnlyList<int> ActiveProcessIds()
    {
        if (_stale)
        {
            _stale = false;
            Unwatch();
            Watch();
        }

        var processIds = new HashSet<int>();
        foreach (IAudioSessionManager2 manager in _managers)
        {
            foreach (IAudioSessionControl2 session in Sessions(manager))
            {
                if (session.GetState(out int state) == 0 && state == CoreAudio.AudioSessionStateActive
                    && session.IsSystemSoundsSession() != CoreAudio.S_OK
                    && session.GetProcessId(out uint processId) == 0)
                {
                    processIds.Add((int)processId);
                }
            }
        }
        return [.. processIds];
    }

    public void Dispose()
    {
        Unwatch();
        _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications);
    }

    private void Watch()
    {
        if (_enumerator.EnumAudioEndpoints(EDataFlow.Capture, CoreAudio.DEVICE_STATE_ACTIVE, out IMMDeviceCollection devices) != 0
            || devices.GetCount(out uint count) != 0)
        {
            return;
        }

        for (uint i = 0; i < count; i++)
        {
            if (devices.Item(i, out IMMDevice device) != 0
                || device.Activate(typeof(IAudioSessionManager2).GUID, CoreAudio.CLSCTX_ALL, 0, out nint pointer) != 0)
            {
                continue;
            }

            var manager = ComPointer.TakeOwnership<IAudioSessionManager2>(pointer);
            // Sessions report starting and stopping to their own listeners; the manager reports new sessions (and
            // only once its session list has been asked for, which Sessions does).
            foreach (IAudioSessionControl2 session in Sessions(manager))
            {
                session.RegisterAudioSessionNotification(_sessionNotifications);
                _sessions.Add(session);
            }
            manager.RegisterSessionNotification(_sessionNotifications);
            _managers.Add(manager);
        }
    }

    private void Unwatch()
    {
        foreach (IAudioSessionControl2 session in _sessions)
            session.UnregisterAudioSessionNotification(_sessionNotifications);
        foreach (IAudioSessionManager2 manager in _managers)
            manager.UnregisterSessionNotification(_sessionNotifications);
        _sessions.Clear();
        _managers.Clear();
    }

    private static List<IAudioSessionControl2> Sessions(IAudioSessionManager2 manager)
    {
        var sessions = new List<IAudioSessionControl2>();
        if (manager.GetSessionEnumerator(out IAudioSessionEnumerator enumerator) != 0 || enumerator.GetCount(out int count) != 0)
            return sessions;

        for (int i = 0; i < count; i++)
        {
            if (enumerator.GetSession(i, out IAudioSessionControl2 session) == 0)
                sessions.Add(session);
        }
        return sessions;
    }
}
