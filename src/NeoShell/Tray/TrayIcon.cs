using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace NeoShell.Tray;

/// <summary>A tray icon as the taskbar shows it.</summary>
internal sealed class TrayIcon(string key) : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = key;

    public TrayIconState State { get; private set; } = new(0, 0, null, 0, 0, "", false, 0, false);

    public ImageSource? Icon => _icon;

    public string Name => State.Tip.Length > 0 ? State.Tip : "Notification icon";

    /// <summary>
    /// The standard tooltip, or null for version 4 icons that show their own popup instead (unless they ask for the
    /// standard one).
    /// </summary>
    public string? ToolTip => State.Tip.Length == 0 || (State.Version >= 4 && !State.ShowTip) ? null : State.Tip;

    public void Update(TrayIconState state, ImageSource? icon)
    {
        State = state;
        _icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // all properties
    }

    // UI Automation names list items by their ToString.
    public override string ToString() => Name;
}
