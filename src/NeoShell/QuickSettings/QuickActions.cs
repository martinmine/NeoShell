using NeoShell.Interop.Display;
using NeoShell.Interop.Shell;
using NeoShell.Logging;

namespace NeoShell.QuickSettings;

/// <summary>A quick action's tile as Windows reports it.</summary>
/// <param name="IsShown">The PC has it (the setting is applicable).</param>
/// <param name="IsAvailable">It can be switched now (the setting is enabled).</param>
/// <param name="Label">What the tile says under it when it isn't its name (the hotspot's state); null for the name.</param>
/// <param name="HasSwitch">A VPN tile switches the last VPN only while there's one to switch.</param>
/// <param name="HasPage">Whether it opens a page: the VPN's always, nearby sharing's while Windows offers it.</param>
public readonly record struct QuickActionState(
    bool IsShown, bool IsAvailable, bool IsOn, string? Label = null, bool HasSwitch = true, bool HasPage = false)
{
    public static readonly QuickActionState Hidden = new(false, false, false);
}

/// <summary>
/// What <see cref="QuickActions"/> last read; <see cref="Brightness"/> is null without a brightness to set, and
/// <see cref="HasAirplaneMode"/> says whether Windows offers airplane mode here (it doesn't without radios).
/// <see cref="Cast"/> is the Cast tile's label and state: "Wired display", on, while a cable's display is in use.
/// <see cref="ColorFilters"/> and <see cref="MonoAudio"/> are the Accessibility page's switches.
/// </summary>
internal sealed record QuickActionsState(
    QuickActionState NightLight,
    QuickActionState NearbySharing,
    QuickActionState MobileHotspot,
    QuickActionState Vpn,
    QuickActionState RotationLock,
    QuickActionState Cast,
    int? Brightness,
    bool HasAirplaneMode,
    bool ColorFilters,
    bool MonoAudio)
{
    public static readonly QuickActionsState None = new(
        QuickActionState.Hidden, QuickActionState.Hidden, QuickActionState.Hidden, QuickActionState.Hidden, QuickActionState.Hidden,
        QuickActionState.Hidden, null, false, false, false);
}

/// <summary>
/// Quick Settings' tiles whose state Windows keeps in its own stores (night light in the cloud data store, nearby
/// sharing in the Connected Devices Platform, the hotspot and VPN in the network service), read and switched through
/// the same Settings handlers Windows' Quick Settings uses, so they work as the shell too, and shown where Windows'
/// settings environment says the PC has them, as Windows' Quick Settings does; colour filters and mono audio too, which
/// Windows' Accessibility page switches as quick actions of their own. They're opened in the background at start;
/// <see cref="Changed"/> comes on a thread-pool thread.
/// </summary>
internal sealed class QuickActions : IDisposable
{
    private const string NightLightId = "SystemSettings_Display_BlueLight_ManualToggleQuickAction";
    private const string NearbySharingId = "SystemSettings_SharedExperiences_NearShareQuickAction";
    private const string MobileHotspotId = "SystemSettings_Network_Tethering_QuickAction";
    private const string VpnId = "SystemSettings_Network_VPN_QuickAction";
    private const string AirplaneModeId = "SystemSettings_Radio_IsAirplaneModeEnabled";
    private const string CastId = "SystemSettings_DeviceDiscovery_Connect_QuickAction";
    // Microsoft.QuickAction.ColorFilters and .MonoMix, the Accessibility page's (not SettingsHandlers_Accessibility's
    // ColorFilter_IsEnabled, Settings' own switch for the same state).
    private const string ColorFiltersId = "SystemSettings_Accessibility_ColorFiltering_IsEnabled";
    private const string MonoAudioId = "SystemSettings_Accessibility_IsAudioMonoMixStateEnabled";
    // Where the audio service keeps mono audio: its handler reports only the changes made through itself.
    private const string MonoAudioKey = @"Software\Microsoft\Multimedia\Audio";

    private SettingsEnvironment? _environment;
    private SystemSetting? _nightLight;
    private SystemSetting? _nearbySharing;
    private SystemSetting? _mobileHotspot;
    private SystemSetting? _vpn;
    private SystemSetting? _rotationLock;
    private SystemSetting? _cast;
    private SystemSetting? _brightness;
    private SystemSetting? _colorFilters;
    private SystemSetting? _monoAudio;
    private RegistryWatcher? _monoAudioWatcher;
    private int _refreshQueued;
    private int _brightnessWanted = -1;
    private int _brightnessQueued;
    private volatile bool _disposed;

    public QuickActions() => ThreadPool.QueueUserWorkItem(_ => Open());

    /// <summary>Raised on a thread-pool thread after <see cref="State"/> changed.</summary>
    public event Action? Changed;

    public QuickActionsState State { get; private set; } = QuickActionsState.None;

    public void SetNightLight(bool on) => Change(_nightLight, "Night light", on, setting => setting.SetValue(on));

    public void SetNearbySharing(bool on) => Change(_nearbySharing, "Nearby sharing", on, setting => setting.SetValue(on));

    // The hotspot's handler shares the internet connection with the settings saved in Settings.
    public void SetMobileHotspot(bool on) => Change(_mobileHotspot, "Mobile hotspot", on, setting => setting.SetProperty("Value", on));

    // Connects the VPN Windows last used, or hangs it up; the value itself is ignored.
    public void ToggleVpn(bool on) => Change(_vpn, "VPN", on, setting => setting.SetProperty("Value", on));

    public void SetRotationLock(bool on) => Change(_rotationLock, "Rotation lock", on, setting => setting.SetValue(on));

    public void SetColorFilters(bool on) => Change(_colorFilters, "Colour filters", on, setting => setting.SetValue(on));

    public void SetMonoAudio(bool on) => Change(_monoAudio, "Mono audio", on, setting => setting.SetValue(on));

    /// <summary>
    /// Reads everything again, in the background: whenever Quick Settings opens, as Windows' settings environment
    /// learns some answers (whether there's a VPN) a moment after it's asked and says nothing when they change.
    /// </summary>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => ReadState());
    }

    /// <summary>Sets the brightness (0 to 100); while a slider is dragged only the latest value is sent.</summary>
    public void SetBrightness(int percent)
    {
        Interlocked.Exchange(ref _brightnessWanted, percent);
        if (Interlocked.Exchange(ref _brightnessQueued, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => SendBrightness());
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (SystemSetting? setting in (SystemSetting?[])[_nightLight, _nearbySharing, _mobileHotspot, _vpn, _rotationLock, _cast, _brightness, _colorFilters, _monoAudio])
            setting?.Dispose();
        _monoAudioWatcher?.Dispose();
        _environment?.Dispose();
    }

    private void Open()
    {
        try
        {
            _environment = SettingsEnvironment.Open();
        }
        catch (Exception ex)
        {
            Log.Warn("Quick Settings: could not open Windows' settings environment", ex);
        }
        _nightLight = Open(NightLightId);
        _nearbySharing = Open(NearbySharingId);
        _mobileHotspot = Open(MobileHotspotId);
        _vpn = Open(VpnId);
        _rotationLock = Open("SystemSettings_Display_IsRotationLockedQuickAction");
        _cast = Open(CastId);
        // Quick Settings' slider (Microsoft.QuickAction.Brightness). Not SystemSettings_System_Display_Internal_Brightness:
        // opened in another process than Settings', that handler fails fast a few seconds later.
        _brightness = Open("SystemSettings_Display_Brightness");
        _colorFilters = Open(ColorFiltersId);
        _monoAudio = Open(MonoAudioId);
        _monoAudioWatcher = new RegistryWatcher(MonoAudioKey);
        _monoAudioWatcher.Changed += Refresh;
        if (_disposed)
            Dispose();
        else
            ReadState();
    }

    private SystemSetting? Open(string id)
    {
        try
        {
            SystemSetting? setting = SystemSetting.Open(id);
            if (setting is null)
                Log.Info($"Quick Settings: Windows has no {id}");
            else
                setting.Changed += Refresh;
            return setting;
        }
        catch (Exception ex)
        {
            Log.Warn($"Quick Settings: could not open {id}", ex);
            return null;
        }
    }

    private void ReadState()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        if (_disposed)
            return;

        try
        {
            State = new QuickActionsState(
                Read(_nightLight, NightLightId, setting => setting.GetValue() is true),
                Read(_nearbySharing, NearbySharingId, setting => setting.GetValue() is true) with
                {
                    // Its page only while Windows' Quick Settings offers it (shows its chevron).
                    HasPage = _nearbySharing?.GetValue("QuickActionIsL2TemplateVisible") is true,
                },
                Read(_mobileHotspot, MobileHotspotId, setting => setting.GetProperty("QuickActionIsActive") is true) with
                {
                    Label = _mobileHotspot?.GetProperty("QuickActionStatus") as string,
                },
                Read(_vpn, VpnId, setting => setting.GetProperty("QuickActionIsActive") is true) with
                {
                    Label = _vpn?.GetProperty("QuickActionStatus") as string,
                    HasSwitch = _vpn?.GetProperty("QuickActionIsToggleTemplateVisible") is true,
                    HasPage = true,
                },
                ReadRotationLock(),
                Read(_cast, CastId, setting => setting.GetValue("QuickActionIsActive") is true) with
                {
                    Label = _cast?.GetValue("QuickActionStatus") as string is { Length: > 0 } status ? status : null,
                },
                _brightness is { IsApplicable: true } brightness && brightness.GetValue() is int percent ? percent : null,
                IsApplicable(AirplaneModeId),
                _colorFilters?.GetValue() is true,
                _monoAudio?.GetValue() is true);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("Quick Settings: reading Windows' quick actions failed", ex);
        }
    }

    private QuickActionState Read(SystemSetting? setting, string id, Func<SystemSetting, bool> isOn) =>
        setting is { IsApplicable: true } && IsApplicable(id)
            ? new QuickActionState(true, setting.IsEnabled, isOn(setting))
            : QuickActionState.Hidden;

    private bool IsApplicable(string id) => _environment?.IsQuickActionApplicable(id) ?? true;

    // Its handler counts any PC as having it; the tile is only for PCs whose screen can turn (an orientation sensor).
    private QuickActionState ReadRotationLock() =>
        _rotationLock is null ? QuickActionState.Hidden : QuickSettingsDisplay.RotationLockTile(AutoRotation.Current());

    private void Change(SystemSetting? setting, string name, bool on, Action<SystemSetting> change)
    {
        if (setting is null)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                change(setting);
                Log.Info($"{name}: {(on ? "on" : "off")}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not turn {name} {(on ? "on" : "off")}", ex);
            }
            Refresh();
        });
    }

    private void SendBrightness()
    {
        Interlocked.Exchange(ref _brightnessQueued, 0);
        int percent = Interlocked.Exchange(ref _brightnessWanted, -1);
        if (percent < 0 || _brightness is not { } brightness)
            return;

        try
        {
            brightness.SetValue(percent);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not set the brightness to {percent}%", ex);
        }
    }
}
