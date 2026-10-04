using System.Diagnostics;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Network;
using NeoShell.Interop.Shell;
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
    private readonly AudioMixer? _mixer;
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
            _mixer = new AudioMixer();
            _mixer.Changed += QueueUpdate;
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

    /// <summary>The sound outputs, to choose the default from; empty when they can't be read.</summary>
    public IReadOnlyList<AudioDevice> OutputDevices()
    {
        if (_audio is null)
            return [];

        try
        {
            return AudioDevices.Outputs();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not list the sound outputs", ex);
            return [];
        }
    }

    /// <summary>Makes Windows play through another output; the volume follows it (<see cref="Changed"/>).</summary>
    public void SetOutputDevice(AudioDevice device)
    {
        try
        {
            AudioDevices.SetDefault(device.Id);
            Log.Info($"Sound output: {device.Name}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not make {device.Name} the sound output", ex);
        }
    }

    /// <summary>The apps playing on the default output, for the mixer: read fresh each time.</summary>
    public IReadOnlyList<AudioApp> MixerApps() => _mixer?.Apps() ?? [];

    /// <summary>Names of the apps recording from a microphone; empty while none is.</summary>
    public IReadOnlyList<string> MicrophoneApps { get; private set; } = [];

    public void Dispose()
    {
        _network.Dispose();
        _audio?.Dispose();
        _capture?.Dispose();
        _mixer?.Dispose();
    }

    /// <summary>The name the mixer shows: the session's own, else the app's, else the executable's description.</summary>
    public static string AppName(AudioApp app)
    {
        if (app.Name is { } name)
            return name;
        if (app.IsSystemSounds)
            return "System sounds";
        if (app.PackageAppId is { } appId && ShellItems.GetDisplayName(ShellItems.AppsFolderPath(appId)) is { } packaged)
            return packaged;
        if (app.ProcessPath is not { } path)
            return "Unknown app";

        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description;
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    // Called from Windows' threads.
    private void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
            _dispatcher.Post(Update);
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
