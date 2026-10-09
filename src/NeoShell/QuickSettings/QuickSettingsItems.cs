using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Bluetooth;
using NeoShell.Interop.Network;
using NeoShell.Interop.Power;

namespace NeoShell.QuickSettings;

/// <summary>Raises <see cref="PropertyChanged"/> for the Quick Settings rows.</summary>
internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A network on the Wi-Fi page; choosing it opens it up to connect (or disconnect).</summary>
internal sealed class WifiItem(WifiNetwork network) : Observable
{
    private bool _isExpanded;
    private bool _needsPassword;
    private bool _isBusy;
    private string? _message;
    private bool _connectAutomatically = true;
    private string _password = "";

    public WifiNetwork Network { get; } = network;

    public string Name => Network.Ssid;

    public string Glyph => QuickSettingsDisplay.WifiGlyph(Network.SignalBars);

    public Visibility LockVisibility => Network.IsSecured ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"Connected, secured", or what's happening while connecting.</summary>
    public string Status => _message ?? QuickSettingsDisplay.WifiStatus(Network);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
                Raise(nameof(ExpandedVisibility));
        }
    }

    public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Windows has no key for it: the box for one shows, and the button says Next.</summary>
    public bool NeedsPassword
    {
        get => _needsPassword;
        set
        {
            if (Set(ref _needsPassword, value))
            {
                Raise(nameof(PasswordVisibility));
                Raise(nameof(ActionText));
            }
        }
    }

    public Visibility PasswordVisibility => NeedsPassword ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ConnectVisibility => Network.IsConnected ? Visibility.Collapsed : Visibility.Visible;

    public bool ConnectAutomatically { get => _connectAutomatically; set => Set(ref _connectAutomatically, value); }

    public string Password { get => _password; set => Set(ref _password, value); }

    public string ActionText => Network.IsConnected ? "Disconnect" : NeedsPassword ? "Next" : "Connect";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                Raise(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    /// <summary>Shown in place of the status; null shows the status again.</summary>
    public string? Message
    {
        get => _message;
        set
        {
            if (Set(ref _message, value))
                Raise(nameof(Status));
        }
    }

    public override string ToString() => Name;
}

/// <summary>A VPN connection on the VPN page; choosing it opens it up to connect or disconnect.</summary>
internal sealed class VpnItem(VpnConnection connection) : Observable
{
    private bool _isExpanded;

    public VpnConnection Connection { get; } = connection;

    public string Name => Connection.Name;

    public string Status => Connection.IsConnected ? "Connected" : "";

    public Visibility StatusVisibility => Connection.IsConnected ? Visibility.Visible : Visibility.Collapsed;

    public string ActionText => Connection.IsConnected ? "Disconnect" : "Connect";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
                Raise(nameof(ExpandedVisibility));
        }
    }

    public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public override string ToString() => Name;
}

/// <summary>A paired device on the Bluetooth page; an audio device connects when chosen, or opens to disconnect.</summary>
internal sealed class BluetoothItem(PairedDevice device) : Observable
{
    private PairedDevice _device = device;
    private BluetoothActivity _activity;
    private bool _isExpanded;

    /// <summary>The device as last read; replaced while connecting, as its state changes.</summary>
    public PairedDevice Device
    {
        get => _device;
        set
        {
            if (Set(ref _device, value))
                RaiseState();
        }
    }

    public BluetoothActivity Activity
    {
        get => _activity;
        set
        {
            if (Set(ref _activity, value))
                RaiseState();
        }
    }

    public string Name => Device.Name;

    public string Glyph => QuickSettingsDisplay.BluetoothGlyph(Device.Kind);

    public string Status => QuickSettingsDisplay.BluetoothStatus(Device, Activity);

    public string DeviceBatteryText => QuickSettingsDisplay.BluetoothBattery(Device) ?? "";

    public string DeviceBatteryGlyph => QuickSettingsDisplay.BatteryGlyph(new BatteryState(Device.Battery ?? 0, false));

    public Visibility DeviceBatteryVisibility => QuickSettingsDisplay.BluetoothBattery(Device) is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsBusy => Activity is BluetoothActivity.Connecting or BluetoothActivity.Disconnecting;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
                Raise(nameof(DisconnectVisibility));
        }
    }

    /// <summary>The opened row of a connected audio device: Disconnect.</summary>
    public Visibility DisconnectVisibility =>
        IsExpanded && QuickSettingsDisplay.BluetoothChoose(Device) == BluetoothChoice.OfferDisconnect ? Visibility.Visible : Visibility.Collapsed;

    public bool IsIdle => !IsBusy;

    private void RaiseState()
    {
        foreach (string name in (string[])[nameof(Status), nameof(DeviceBatteryText), nameof(DeviceBatteryGlyph), nameof(DeviceBatteryVisibility),
            nameof(DisconnectVisibility), nameof(IsIdle)])
        {
            Raise(name);
        }
    }

    public override string ToString() => Name;
}

/// <summary>A row on the Accessibility page: a feature with its switch.</summary>
internal sealed class AssistiveFeature : Observable
{
    private bool _isOn;
    private bool _refreshing;

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Glyph { get; init; }

    public required string AutomationId { get; init; }

    /// <summary>Reads whether it's on.</summary>
    public required Func<bool> Read { get; init; }

    /// <summary>Turns it on or off.</summary>
    public required Action<bool> Write { get; init; }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (!Set(ref _isOn, value))
                return;

            Raise(nameof(StateText));
            if (!_refreshing)
                Write(value);
        }
    }

    public string StateText => IsOn ? "On" : "Off";

    /// <summary>Shows the feature's current state without switching anything.</summary>
    public void Refresh()
    {
        _refreshing = true;
        IsOn = Read();
        _refreshing = false;
    }

    public override string ToString() => Name;
}
