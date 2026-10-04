using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Bluetooth;
using NeoShell.Interop.Network;

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

/// <summary>A paired device on the Bluetooth page.</summary>
internal sealed class BluetoothItem(PairedDevice device)
{
    public string Name => device.Name;

    public string Glyph => QuickSettingsDisplay.BluetoothGlyph(device.Kind);

    public string Status => device.IsConnected ? "Connected" : "Paired";

    public override string ToString() => Name;
}

/// <summary>
/// A row on the Accessibility page: a feature with its switch, or, for features Windows offers no way to switch
/// from outside Settings, a link to its page there.
/// </summary>
internal sealed class AssistiveFeature : Observable
{
    private bool _isOn;
    private bool _refreshing;

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Glyph { get; init; }

    public required string AutomationId { get; init; }

    /// <summary>Reads whether it's on; null for a link.</summary>
    public Func<bool>? Read { get; init; }

    /// <summary>Turns it on or off; null for a link.</summary>
    public Action<bool>? Write { get; init; }

    /// <summary>The feature's page in Settings, for a link.</summary>
    public string? SettingsUri { get; init; }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (!Set(ref _isOn, value))
                return;

            Raise(nameof(StateText));
            if (!_refreshing)
                Write?.Invoke(value);
        }
    }

    public string StateText => IsOn ? "On" : "Off";

    public Visibility SwitchVisibility => Write is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LinkVisibility => Write is null ? Visibility.Visible : Visibility.Collapsed;

    public string LinkName => $"{Name} settings";

    /// <summary>Shows the feature's current state without switching anything.</summary>
    public void Refresh()
    {
        if (Read is null)
            return;

        _refreshing = true;
        IsOn = Read();
        _refreshing = false;
    }

    public override string ToString() => Name;
}
