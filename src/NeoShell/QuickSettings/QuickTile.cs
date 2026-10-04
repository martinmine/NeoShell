using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NeoShell.QuickSettings;

/// <summary>
/// A Quick Settings button: a switch (Airplane mode), a page (Accessibility), or both split in two halves (Wi-Fi:
/// the left half switches, the right opens the page). The label sits under it.
/// </summary>
internal sealed class QuickTile(QuickTileKind kind, string label, string glyph) : INotifyPropertyChanged
{
    private string _label = label;
    private bool _isOn;
    private bool _isAvailable = true;
    private bool _isShown = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public QuickTileKind Kind { get; } = kind;

    public string Glyph { get; } = glyph;

    /// <summary>The feature's name, or what it's connected to (the Wi-Fi network's name).</summary>
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>The name for UI Automation and the tooltip: the feature's own, whatever the label shows.</summary>
    public required string Name { get; init; }

    public bool HasSwitch { get; init; }

    public bool HasPage { get; init; }

    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }

    /// <summary>False while it can't be used (Wi-Fi in airplane mode with no radio left on).</summary>
    public bool IsAvailable { get => _isAvailable; set => Set(ref _isAvailable, value); }

    /// <summary>Whether the PC has it at all: tiles without hardware or support aren't shown, as in Windows.</summary>
    public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

    public bool IsSplit => HasSwitch && HasPage;

    public int MainColumnSpan => IsSplit ? 1 : 2;

    public CornerRadius MainCornerRadius => IsSplit ? new CornerRadius(4, 0, 0, 4) : new CornerRadius(4);

    public Visibility SplitVisibility => IsSplit ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A page-only tile shows its chevron next to the glyph.</summary>
    public Visibility InlineChevronVisibility => HasPage && !HasSwitch ? Visibility.Visible : Visibility.Collapsed;

    public string PageName => $"{Name} settings";

    public string TileId => $"{Kind}Tile";

    public string PageId => $"{Kind}PageButton";

    // UI Automation names list items by their ToString.
    public override string ToString() => Name;

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal enum QuickTileKind
{
    WiFi,
    Bluetooth,
    AirplaneMode,
    Accessibility,
    EnergySaver,
    LiveCaptions,
    NightLight,
    NearbySharing,
    Cast,
    Project,
}
