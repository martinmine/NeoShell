using System.Diagnostics;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Network;
using NeoShell.Logging;

namespace NeoShell.Tray;

/// <summary>
/// The network, volume and microphone state behind the taskbar's indicators. Windows reports changes on its own
/// threads; they arrive here as one <see cref="Changed"/> on the UI thread per burst.
/// </summary>
internal sealed class Indicators : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly NetworkStatus _network = new();
    private readonly AudioEndpoint? _audio;
    private readonly CaptureMonitor? _capture;
    private int _updateQueued;

    public Indicators()
    {
        _network.Changed += QueueUpdate;
        try
        {
            _audio = new AudioEndpoint();
            _audio.Changed += QueueUpdate;
            _capture = new CaptureMonitor();
            _capture.Changed += QueueUpdate;
        }
        catch (Exception ex)
        {
            // The audio service can be stopped; the network indicator still works.
            Log.Warn("Audio indicators unavailable", ex);
        }
        Update();
    }

    /// <summary>Raised on the UI thread after any of the state changed.</summary>
    public event Action? Changed;

    public NetworkState Network { get; private set; } = NetworkState.Disconnected;

    public bool HasAudioDevice => _audio?.HasDevice == true;

    public string? AudioDeviceName => _audio?.DeviceName;

    public float Volume
    {
        get => _audio?.Volume ?? 0;
        set
        {
            if (_audio is not null)
                _audio.Volume = value;
        }
    }

    public bool IsMuted
    {
        get => _audio?.IsMuted == true;
        set
        {
            if (_audio is not null)
                _audio.IsMuted = value;
        }
    }

    /// <summary>Names of the apps recording from a microphone; empty while none is.</summary>
    public IReadOnlyList<string> MicrophoneApps { get; private set; } = [];

    public void Dispose()
    {
        _network.Dispose();
        _audio?.Dispose();
        _capture?.Dispose();
    }

    // Called from Windows' threads.
    private void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
            _dispatcher.TryEnqueue(Update);
    }

    private void Update()
    {
        Interlocked.Exchange(ref _updateQueued, 0);
        try
        {
            Network = NetworkStatus.Read();
            MicrophoneApps = [.. (_capture?.ActiveProcessIds() ?? []).Select(AppName).Distinct()];
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Updating the indicators failed", ex);
        }
    }

    private static string AppName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string? description = null;
            try
            {
                description = process.MainModule?.FileVersionInfo.FileDescription;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Elevated and protected processes don't let us read their modules.
            }
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
        }
        catch (ArgumentException)
        {
            return $"Process {processId}"; // already gone
        }
    }
}
