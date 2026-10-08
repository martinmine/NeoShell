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
    private bool _hasSwitch;
    private bool _hasPage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public QuickTileKind Kind { get; } = kind;

    /// <summary>The glyph, or <see cref="OnGlyph"/> while it's on.</summary>
    public string Glyph => IsOn && OnGlyph is not null ? OnGlyph : glyph;

    public double GlyphSize => IsOn && OnGlyph is not null ? OnGlyphSize : OffGlyphSize;

    /// <summary>
    /// Windows draws some tiles' icons as animations whose last frames differ (night light: a sun, a moon while it's
    /// on); a glyph that looks like the one shown while it's on, or null for the same as while it's off.
    /// </summary>
    public string? OnGlyph { get; init; }

    public double OnGlyphSize { get; init; } = 16;

    /// <summary>Some of Windows' animated icons are drawn smaller than its glyphs (night light's).</summary>
    public double OffGlyphSize { get; init; } = 16;

    /// <summary>The feature's name, or what it's connected to (the Wi-Fi network's name).</summary>
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>The name for UI Automation and the tooltip: the feature's own, whatever the label shows.</summary>
    public required string Name { get; init; }

    /// <summary>A VPN tile has its switch only while Windows has a VPN for it to switch.</summary>
    public bool HasSwitch
    {
        get => _hasSwitch;
        set
        {
            if (_hasSwitch == value)
                return;

            _hasSwitch = value;
            RaiseLayoutChanged(nameof(HasSwitch));
        }
    }

    /// <summary>Nearby sharing opens its page only while Windows offers it.</summary>
    public bool HasPage
    {
        get => _hasPage;
        set
        {
            if (_hasPage == value)
                return;

            _hasPage = value;
            RaiseLayoutChanged(nameof(HasPage));
        }
    }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            Set(ref _isOn, value);
            if (OnGlyph is not null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Glyph)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlyphSize)));
            }
        }
    }

    /// <summary>False while it can't be used (Wi-Fi in airplane mode with no radio left on).</summary>
    public bool IsAvailable { get => _isAvailable; set => Set(ref _isAvailable, value); }

    /// <summary>Whether the PC has it at all: tiles without hardware or support aren't shown, as in Windows.</summary>
    public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

    /// <summary>Shows a quick action's state as Windows reports it.</summary>
    public void Show(QuickActionState state)
    {
        IsShown = state.IsShown;
        IsAvailable = state.IsAvailable;
        IsOn = state.IsOn;
        Label = state.Label ?? Name;
        HasSwitch = state.HasSwitch;
        HasPage = state.HasPage;
    }

    public bool IsSplit => HasSwitch && HasPage;

    public int MainColumnSpan => IsSplit ? 1 : 2;

    public CornerRadius MainCornerRadius => IsSplit ? new CornerRadius(4, 0, 0, 4) : new CornerRadius(4);

    public Visibility SplitVisibility => IsSplit ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A page-only tile shows its chevron next to the glyph.</summary>
    public Visibility InlineChevronVisibility => HasPage && !HasSwitch ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The page button's name and tooltip.</summary>
    public string PageName { get; init; } = $"{label} settings";

    public string TileId => $"{Kind}Tile";

    public string PageId => $"{Kind}PageButton";

    // UI Automation names list items by their ToString.
    public override string ToString() => Name;

    private void RaiseLayoutChanged(string name)
    {
        foreach (string each in (string[])[name, nameof(IsSplit), nameof(MainColumnSpan), nameof(MainCornerRadius), nameof(SplitVisibility), nameof(InlineChevronVisibility)])
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(each));
    }

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
    Vpn,
    RotationLock,
    EnergySaver,
    LiveCaptions,
    NightLight,
    MobileHotspot,
    NearbySharing,
    Cast,
    Project,
}
