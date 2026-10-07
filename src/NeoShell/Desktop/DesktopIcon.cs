using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;

namespace NeoShell.Desktop;

/// <summary>An icon on the desktop. Kept across refreshes while its item exists, so selection and images stay.</summary>
internal sealed class DesktopIcon(DesktopEntry entry) : INotifyPropertyChanged
{
    /// <summary>Explorer keeps the shortcut arrow at most medium-icon size; on large icons it stays in the corner.</summary>
    public const double MaxOverlaySize = 48;

    private DesktopEntry _entry = entry;
    private ImageSource? _image;
    private ImageSource? _overlay;
    private double _size;
    private bool _isDropTarget;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DesktopEntry Entry
    {
        get => _entry;
        set
        {
            bool renamed = value.Item.Name != _entry.Item.Name;
            _entry = value;
            if (renamed)
                OnPropertyChanged(nameof(Name));
        }
    }

    public DesktopItem Item => _entry.Item;

    public string Name => _entry.Item.Name;

    public ImageSource? Image
    {
        get => _image;
        set => Set(ref _image, value);
    }

    /// <summary>The shortcut arrow, on shortcuts only.</summary>
    public ImageSource? Overlay
    {
        get => _overlay;
        set => Set(ref _overlay, value);
    }

    /// <summary>Icon size in effective pixels.</summary>
    public double Size
    {
        get => _size;
        set
        {
            if (_size == value)
                return;
            _size = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlaySize));
        }
    }

    public double OverlaySize => Math.Min(_size, MaxOverlaySize);

    /// <summary>Where the icon is: a monitor and a cell of its grid; set by <see cref="DesktopIcons"/>, null until placed.</summary>
    public IconPlace? Place { get; set; }

    /// <summary>The icon's cell on its monitor's grid.</summary>
    public GridCell Cell => Place?.Cell ?? default;

    /// <summary>The name and flags Explorer's saved layouts know the item by (<see cref="IconLayouts"/>).</summary>
    public string LayoutName { get; set; } = "";

    public LayoutIconFlags LayoutFlags { get; set; }

    /// <summary>The pixel size <see cref="Image"/> was loaded at, for the DPI of the monitor the icon is on.</summary>
    public int? ImagePixels { get; set; }

    public int? OverlayPixels { get; set; }

    /// <summary>In the desktop's selection, which spans every monitor.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>A drag is over the icon and would drop on it.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
