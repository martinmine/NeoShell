using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NeoShell.Settings;

namespace NeoShell.StartMenu;

/// <summary>An app or a file in Start: pinned, in All apps, or a search result; or a folder of pinned apps.</summary>
/// <param name="Target">What opening the item launches; for an app, also what pinning keeps.</param>
internal sealed class StartItem(PinnedApp target, string subtitle, bool isApp, AppIcons icons) : INotifyPropertyChanged
{
    private bool _iconRequested;

    /// <summary>A folder of pinned apps, showing its first four apps' icons.</summary>
    public StartItem(StartFolder folder, AppIcons icons)
        : this(new PinnedApp(StartPins.DisplayName(folder)), "", isApp: false, icons)
    {
        Folder = folder;
        MiniApps = [.. folder.Apps.Take(4).Select(app => new StartItem(app, "App", isApp: true, icons))];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PinnedApp Target { get; } = target;

    public string Title => Target.DisplayName;

    public string Subtitle { get; } = subtitle;

    public bool IsApp { get; } = isApp;

    public StartFolder? Folder { get; }

    public bool IsFolder => Folder is not null;

    public string AutomationId => Folder is null ? "PinnedApp" : "PinnedFolder";

    /// <summary>A folder's plate shows; an app's is there only for when another app is held over it.</summary>
    public double PlateOpacity => Folder is null ? 0 : 1;

    /// <summary>The apps whose icons a folder shows, two by two.</summary>
    public IReadOnlyList<StartItem> MiniApps { get; } = [];

    /// <summary>Loads the icon the first time something shows the item, so only visible items cost a load.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (Folder is not null)
                return null;
            _iconRequested = true;
            return icons.Get(Target);
        }
    }

    /// <summary>Tells bindings to look again after icons have loaded.</summary>
    public void RefreshIcon()
    {
        if (_iconRequested)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        foreach (StartItem app in MiniApps)
            app.RefreshIcon();
    }

    // UI Automation names list items by their ToString.
    public override string ToString() => Title;
}
