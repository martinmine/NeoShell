using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NeoShell.Settings;

namespace NeoShell.StartMenu;

/// <summary>An app or a file in Start: pinned, in All apps, or a search result.</summary>
/// <param name="Target">What opening the item launches; for an app, also what pinning keeps.</param>
internal sealed class StartItem(PinnedApp target, string subtitle, bool isApp, AppIcons icons) : INotifyPropertyChanged
{
    private bool _iconRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PinnedApp Target { get; } = target;

    public string Title => Target.DisplayName;

    public string Subtitle { get; } = subtitle;

    public bool IsApp { get; } = isApp;

    /// <summary>Loads the icon the first time something shows the item, so only visible items cost a load.</summary>
    public ImageSource? Icon
    {
        get
        {
            _iconRequested = true;
            return icons.Get(Target);
        }
    }

    /// <summary>Tells bindings to look again after icons have loaded.</summary>
    public void RefreshIcon()
    {
        if (_iconRequested)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    // UI Automation names list items by their ToString.
    public override string ToString() => Title;
}
