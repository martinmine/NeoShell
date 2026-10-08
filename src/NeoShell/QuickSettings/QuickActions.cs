using NeoShell.Interop.Display;
using NeoShell.Interop.Shell;
using NeoShell.Logging;

namespace NeoShell.QuickSettings;

/// <summary>A quick action's tile as Windows reports it.</summary>
/// <param name="IsShown">The PC has it (the setting is applicable).</param>
/// <param name="IsAvailable">It can be switched now (the setting is enabled).</param>
/// <param name="Label">What the tile says under it when it isn't its name (the hotspot's state); null for the name.</param>
/// <param name="HasSwitch">A VPN tile switches the last VPN only while there's one to switch.</param>
public readonly record struct QuickActionState(bool IsShown, bool IsAvailable, bool IsOn, string? Label = null, bool HasSwitch = true)
{
    public static readonly QuickActionState Hidden = new(false, false, false);
}

/// <summary>What <see cref="QuickActions"/> last read; <see cref="Brightness"/> is null without a brightness to set.</summary>
internal sealed record QuickActionsState(
    QuickActionState NightLight,
    QuickActionState NearbySharing,
    QuickActionState MobileHotspot,
    QuickActionState Vpn,
    QuickActionState RotationLock,
    int? Brightness)
{
    public static readonly QuickActionsState None = new(
        QuickActionState.Hidden, QuickActionState.Hidden, QuickActionState.Hidden, QuickActionState.Hidden, QuickActionState.Hidden, null);
}

/// <summary>
/// Quick Settings' tiles whose state Windows keeps in its own stores (night light in the cloud data store, nearby
/// sharing in the Connected Devices Platform, the hotspot and VPN in the network service), read and switched through
/// the same Settings handlers Windows' Quick Settings uses, so they work as the shell too. They're opened in the
/// background at start; <see cref="Changed"/> comes on a thread-pool thread.
/// </summary>
internal sealed class QuickActions : IDisposable
{
    private SystemSetting? _nightLight;
    private SystemSetting? _nearbySharing;
    private SystemSetting? _mobileHotspot;
    private SystemSetting? _vpn;
    private SystemSetting? _rotationLock;
    private SystemSetting? _brightness;
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
        foreach (SystemSetting? setting in (SystemSetting?[])[_nightLight, _nearbySharing, _mobileHotspot, _vpn, _rotationLock, _brightness])
            setting?.Dispose();
    }

    private void Open()
    {
        _nightLight = Open("SystemSettings_Display_BlueLight_ManualToggleQuickAction");
        _nearbySharing = Open("SystemSettings_SharedExperiences_NearShareQuickAction");
        _mobileHotspot = Open("SystemSettings_Network_Tethering_QuickAction");
        _vpn = Open("SystemSettings_Network_VPN_QuickAction");
        _rotationLock = Open("SystemSettings_Display_IsRotationLockedQuickAction");
        // Quick Settings' slider (Microsoft.QuickAction.Brightness). Not SystemSettings_System_Display_Internal_Brightness:
        // opened in another process than Settings', that handler fails fast a few seconds later.
        _brightness = Open("SystemSettings_Display_Brightness");
        if (_disposed)
            Dispose();
        else
            Refresh();
    }

    private SystemSetting? Open(string id)
    {
        try
        {
            SystemSetting? setting = SystemSetting.Open(id);
            if (setting is null)
                Log.Info($"Quick Settings: Windows has no {id}");
            else
                setting.Changed += QueueRefresh;
            return setting;
        }
        catch (Exception ex)
        {
            Log.Warn($"Quick Settings: could not open {id}", ex);
            return null;
        }
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => Refresh());
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        if (_disposed)
            return;

        try
        {
            State = new QuickActionsState(
                Read(_nightLight, setting => setting.GetValue() is true),
                Read(_nearbySharing, setting => setting.GetValue() is true),
                Read(_mobileHotspot, setting => setting.GetProperty("QuickActionIsActive") is true) with
                {
                    Label = _mobileHotspot?.GetProperty("QuickActionStatus") as string,
                },
                Read(_vpn, setting => setting.GetProperty("QuickActionIsActive") is true) with
                {
                    Label = _vpn?.GetProperty("QuickActionStatus") as string,
                    HasSwitch = _vpn?.GetProperty("QuickActionIsToggleTemplateVisible") is true,
                },
                ReadRotationLock(),
                _brightness is { IsApplicable: true } brightness && brightness.GetValue() is int percent ? percent : null);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("Quick Settings: reading Windows' quick actions failed", ex);
        }
    }

    private static QuickActionState Read(SystemSetting? setting, Func<SystemSetting, bool> isOn) =>
        setting is { IsApplicable: true } ? new QuickActionState(true, setting.IsEnabled, isOn(setting)) : QuickActionState.Hidden;

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
            QueueRefresh();
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
