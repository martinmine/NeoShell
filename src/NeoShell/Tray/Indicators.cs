using System.Diagnostics;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Input;
using NeoShell.Interop.Network;
using NeoShell.Interop.Power;
using NeoShell.Interop.Privacy;
using NeoShell.Interop.Radios;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.QuickSettings;

namespace NeoShell.Tray;

/// <summary>
/// The state behind the taskbar's indicators and Quick Settings: network, volume, the apps using the microphone and
/// the location, radios, airplane mode, energy saver, battery and the input method. Windows reports changes on its
/// own threads; they arrive here as one <see cref="Changed"/> on the UI thread per burst.
/// </summary>
internal sealed class Indicators : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly NetworkStatus _network = new();
    private readonly AudioEndpoint? _audio;
    private readonly AudioEndpoint? _microphone;
    private readonly AudioMixer? _mixer;
    private readonly CapabilityUsage _microphoneUsage = new(CapabilityUsage.Microphone);
    private readonly CapabilityUsage _locationUsage = new(CapabilityUsage.Location);
    private readonly RadioSwitches _radios = new();
    private readonly EnergySaver _energySaver = new();
    private readonly BatteryMonitor _battery = new();
    private readonly InputMethods? _inputMethods;
    private readonly QuickActions _quickActions = new();
    // How many input methods the switcher counted when the list was last read.
    private int _listedInputMethods = -1;
    private int _updateQueued;
    // Airplane mode has no change notification; it's read again when a radio changes.
    private volatile bool _radiosChanged = true;

    public Indicators()
    {
        _network.Changed += QueueUpdate;
        _radios.Changed += OnRadiosChanged;
        _energySaver.Changed += QueueUpdate;
        _battery.Changed += QueueUpdate;
        _microphoneUsage.Changed += QueueUpdate;
        _locationUsage.Changed += QueueUpdate;
        _quickActions.Changed += QueueUpdate;
        try
        {
            _audio = new AudioEndpoint();
            _audio.Changed += QueueUpdate;
            _microphone = new AudioEndpoint(microphone: true);
            _mixer = new AudioMixer();
            _mixer.Changed += QueueUpdate;
        }
        catch (Exception ex)
        {
            // The audio service can be stopped; the network indicator still works.
            Log.Warn("Audio indicators unavailable", ex);
        }
        try
        {
            _inputMethods = new InputMethods();
            _inputMethods.Changed += QueueUpdate;
        }
        catch (Exception ex)
        {
            // InputSwitch.dll is Windows' own and undocumented; without it there's no input indicator.
            Log.Warn("Input indicator unavailable", ex);
        }
        Update();
        _ = RefreshRadiosAsync();
    }

    /// <summary>Raised on the UI thread after any of the state changed.</summary>
    public event Action? Changed;

    /// <summary>Windows' own quick actions (night light, nearby sharing, hotspot, VPN, rotation lock, brightness).</summary>
    public QuickActions QuickActions => _quickActions;

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

    /// <summary>The spatial sound formats the default output offers (Off first) and the one in use; none when unknown.</summary>
    public (IReadOnlyList<SpatialFormat> Formats, string Current) SpatialFormats()
    {
        if (_audio is null)
            return ([], "");

        try
        {
            return SpatialSound.ForDefaultOutput();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the spatial sound formats", ex);
            return ([], "");
        }
    }

    public async Task SetSpatialFormat(SpatialFormat format)
    {
        try
        {
            if (await SpatialSound.SetAsync(format))
                Log.Info($"Spatial sound: {format.Name}");
            else
                Log.Warn($"Windows refused spatial sound {format.Name}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not set spatial sound {format.Name}", ex);
        }
    }

    /// <summary>The apps playing on the default output, for the mixer: read fresh each time.</summary>
    public IReadOnlyList<AudioApp> MixerApps() => _mixer?.Apps() ?? [];

    public bool HasRadio(RadioType type) => _radios.Has(type);

    public bool IsRadioOn(RadioType type) => _radios.IsOn(type);

    /// <summary>Finds the radios again (adapters come and go) and reads airplane mode; raises <see cref="Changed"/>.</summary>
    public async Task RefreshRadiosAsync()
    {
        try
        {
            await _radios.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not find the radios", ex);
        }
        OnRadiosChanged();
    }

    public async Task SetRadioAsync(RadioType type, bool on)
    {
        try
        {
            if (await _radios.SetAsync(type, on))
                Log.Info($"{type}: {(on ? "on" : "off")}");
            else
                Log.Warn($"Windows refused to turn {type} {(on ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not turn {type} {(on ? "on" : "off")}", ex);
        }
    }

    /// <summary>On or off; null without radio management.</summary>
    public bool? AirplaneMode { get; private set; }

    public void SetAirplaneMode(bool on)
    {
        try
        {
            Interop.Radios.AirplaneMode.Set(on);
            Log.Info($"Airplane mode: {(on ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not turn airplane mode {(on ? "on" : "off")}", ex);
        }
        OnRadiosChanged();
    }

    public bool IsEnergySaverAvailable => _energySaver.IsAvailable;

    public bool IsEnergySaverOn => _energySaver.IsOn;

    public void SetEnergySaver(bool on)
    {
        try
        {
            _energySaver.Set(on);
            Log.Info($"Energy saver: {(on ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not turn energy saver {(on ? "on" : "off")}", ex);
        }
    }

    /// <summary>Null on a PC without a battery.</summary>
    public BatteryState? Battery { get; private set; }

    /// <summary>Win+Alt+K: mutes the default microphone, or unmutes it.</summary>
    public void ToggleMicrophoneMute()
    {
        if (_microphone is not { HasDevice: true } microphone)
        {
            Log.Info("Microphone mute: no microphone");
            return;
        }
        microphone.IsMuted = !microphone.IsMuted;
        Log.Info($"Microphone {(microphone.IsMuted ? "muted" : "unmuted")}");
    }

    /// <summary>The input method in front; null while only one is enabled, when the taskbar shows none.</summary>
    public CurrentInputMethod? InputMethod { get; private set; }

    /// <summary>The enabled input methods, in the user's order: read again when their number changes.</summary>
    public IReadOnlyList<InputMethod> EnabledInputMethods { get; private set; } = [];

    /// <summary>The enabled input method that's in front, when it's known.</summary>
    public InputMethod? InputMethodInFront { get; private set; }

    /// <summary>The mode of the IME in front; null for a keyboard layout.</summary>
    public ImeMode? ImeMode { get; private set; }

    /// <summary>Reads the enabled input methods again, as the switcher opens: Windows' list can change without their number.</summary>
    public void RefreshInputMethods()
    {
        if (_inputMethods is null)
            return;

        try
        {
            EnabledInputMethods = Interop.Input.InputMethods.Enabled();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not list the input methods", ex);
        }
        InputMethodInFront = InputMethod is { } current ? IndicatorDisplay.MatchInputMethod(EnabledInputMethods, current) : null;
    }

    /// <summary>Switches the app in front to the input method, as the switcher's click and Win+Space do.</summary>
    public void SwitchInputMethod(InputMethod method)
    {
        try
        {
            _inputMethods?.Activate(method);
            Log.Info($"Input method: {method.Language}, {method.Keyboard}");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Could not switch to {method.Language}, {method.Keyboard}", ex);
        }
    }

    /// <summary>A click on the IME's mode, in screen pixels: the IME switches its mode.</summary>
    public void ClickImeMode(Windows.Graphics.PointInt32 pointer, Windows.Graphics.RectInt32 anchor)
    {
        try
        {
            _inputMethods?.ClickImeMode(pointer, anchor);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException)
        {
            Log.Warn("The IME didn't take the click on its mode", ex);
        }
    }

    /// <summary>The apps using a microphone, by display name; empty while none is.</summary>
    public IReadOnlyList<string> MicrophoneApps => _microphoneUsage.Apps;

    /// <summary>The apps using the location, by display name; empty while none is.</summary>
    public IReadOnlyList<string> LocationApps => _locationUsage.Apps;

    public void Dispose()
    {
        _network.Dispose();
        _radios.Dispose();
        _energySaver.Dispose();
        _battery.Dispose();
        _audio?.Dispose();
        _microphone?.Dispose();
        _microphoneUsage.Dispose();
        _locationUsage.Dispose();
        _mixer?.Dispose();
        _inputMethods?.Dispose();
        _quickActions.Dispose();
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

    private void OnRadiosChanged()
    {
        _radiosChanged = true;
        QueueUpdate();
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
            if (_radiosChanged)
            {
                _radiosChanged = false;
                AirplaneMode = Interop.Radios.AirplaneMode.Read();
            }
            Battery = BatteryMonitor.Read();
            UpdateInputMethod();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Updating the indicators failed", ex);
        }
    }

    private void UpdateInputMethod()
    {
        int count = _inputMethods?.Count ?? 0;
        InputMethod = count > 1 ? _inputMethods!.Current : null;
        ImeMode = InputMethod is { IsTextService: true } ? _inputMethods!.ImeMode : null;
        if (count != _listedInputMethods)
        {
            _listedInputMethods = count;
            RefreshInputMethods();
        }
        else
        {
            InputMethodInFront = InputMethod is { } current ? IndicatorDisplay.MatchInputMethod(EnabledInputMethods, current) : null;
        }
    }
}
