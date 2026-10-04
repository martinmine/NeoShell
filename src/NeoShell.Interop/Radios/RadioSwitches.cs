using Windows.Devices.Radios;

namespace NeoShell.Interop.Radios;

public enum RadioType { WiFi, Bluetooth }

/// <summary>
/// The Wi-Fi and Bluetooth radios' on/off switches (WinRT <c>Windows.Devices.Radios</c>). <see cref="Changed"/> comes
/// from a WinRT thread.
/// </summary>
public sealed class RadioSwitches : IDisposable
{
    private IReadOnlyList<Radio> _radios = [];
    private bool _accessRequested;
    private Task? _refresh;

    /// <summary>A radio was turned on or off, by NeoShell or elsewhere (including by airplane mode).</summary>
    public event Action? Changed;

    /// <summary>Finds the radios again: adapters come and go (a USB dongle, a driver installed).</summary>
    /// <remarks>Called again while it's still looking, it waits for that search rather than starting another.</remarks>
    public Task RefreshAsync() => _refresh is { IsCompleted: false } running ? running : _refresh = FindAsync();

    private async Task FindAsync()
    {
        if (!_accessRequested)
        {
            _accessRequested = true;
            await Radio.RequestAccessAsync();
        }

        IReadOnlyList<Radio> radios = [.. await Radio.GetRadiosAsync()];
        foreach (Radio radio in _radios)
            radio.StateChanged -= OnStateChanged;
        foreach (Radio radio in radios)
            radio.StateChanged += OnStateChanged;
        _radios = radios;
    }

    public bool Has(RadioType type) => Find(type) is not null;

    public bool IsOn(RadioType type) => Find(type)?.State == RadioState.On;

    /// <summary>False when Windows refused (the user turned off apps' access to radios) or there's no such radio.</summary>
    public async Task<bool> SetAsync(RadioType type, bool on) =>
        Find(type) is { } radio && await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;

    public void Dispose()
    {
        foreach (Radio radio in _radios)
            radio.StateChanged -= OnStateChanged;
        _radios = [];
    }

    private Radio? Find(RadioType type)
    {
        RadioKind kind = type == RadioType.WiFi ? RadioKind.WiFi : RadioKind.Bluetooth;
        return _radios.FirstOrDefault(radio => radio.Kind == kind);
    }

    private void OnStateChanged(Radio sender, object args) => Changed?.Invoke();
}
