using System.Collections.ObjectModel;
using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Desktop;

/// <summary>
/// What the desktop's icons show (shell mode only; Explorer has its own otherwise): the desktop's items, kept up to
/// date as files come and go, and the view settings. A <see cref="DesktopIconsView"/> on the primary monitor shows them.
/// </summary>
internal sealed class DesktopIcons : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SettingsStore _settings;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly List<FileSystemWatcher> _watchers = [];
    private int _version;
    private bool _disposed;
    private double _scale = 1;
    private (int Pixels, ImageSource? Image) _overlay;

    public DesktopIcons(SettingsStore settings)
    {
        _settings = settings;
        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(200);
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();

        DesktopLocations locations = DesktopLocations.Current();
        Watch(locations.UserDesktop);
        Watch(locations.PublicDesktop);
        // The Recycle Bin's icon shows whether it's empty; files deleted anywhere change that.
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed))
        {
            if (sid is not null)
                Watch(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid));
        }
    }

    /// <summary>Raised on the UI thread after every refresh, when the icons or the view settings may have changed.</summary>
    public event Action? Refreshed;

    public ObservableCollection<DesktopIcon> Icons { get; } = [];

    public DesktopViewSettings View { get; private set; } = DesktopViewSettings.Read();

    public DesktopSortOrder SortOrder => _settings.Current.DesktopSortOrder;

    /// <summary>Rasterization scale of the monitor showing the icons; images load at its physical pixel size.</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            if (_scale == value)
                return;
            _scale = value;
            QueueRefresh();
        }
    }

    /// <summary>Refreshes shortly, once for a burst of changes.</summary>
    public void QueueRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>Reads the desktop again and reloads every image (they show state, such as a full Recycle Bin).</summary>
    public async Task RefreshAsync()
    {
        _refreshTimer.Stop();
        int version = ++_version;
        try
        {
            DesktopViewSettings view = DesktopViewSettings.Read();
            DesktopLocations locations = DesktopLocations.Current();
            IReadOnlyList<DesktopEntry> entries = await Task.Run(() => Load(view, locations));
            if (version != _version || _disposed)
                return; // a newer refresh took over

            View = view;
            Update(DesktopContents.Sort(entries, SortOrder));
            Refreshed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Refreshing the desktop icons failed", ex);
        }
    }

    public DesktopIcon? Find(string parsingName) =>
        Icons.FirstOrDefault(icon => string.Equals(icon.Item.ParsingName, parsingName, StringComparison.OrdinalIgnoreCase));

    public void SetShowIcons(bool show)
    {
        DesktopViewSettings.SaveShowIcons(show);
        _ = RefreshAsync();
    }

    public void SetIconSize(int size)
    {
        DesktopViewSettings.SaveIconSize(size);
        _ = RefreshAsync();
    }

    public void SetSortOrder(DesktopSortOrder order)
    {
        _settings.Update(_settings.Current with { DesktopSortOrder = order });
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        foreach (FileSystemWatcher watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }

    private static IReadOnlyList<DesktopEntry> Load(DesktopViewSettings view, DesktopLocations locations) =>
    [
        .. DesktopFolder.GetItems(view.ShowHidden, view.ShowProtected)
            .Where(item => DesktopContents.IsShown(item, locations, view.HiddenSystemIcons))
            .DistinctBy(item => item.ParsingName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new DesktopEntry(
                item,
                DesktopContents.SystemIconIndex(item, locations),
                item.Path is { } path && new FileInfo(path) is { Exists: true } file ? file.Length : 0,
                item.Path is { } modifiedPath ? File.GetLastWriteTimeUtc(modifiedPath) : default)),
    ];

    /// <summary>Brings <see cref="Icons"/> in line with the entries, keeping the icons of items still there.</summary>
    private void Update(IReadOnlyList<DesktopEntry> entries)
    {
        var wanted = entries.Select(entry => entry.Item.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Icons.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Icons[i].Item.ParsingName))
                Icons.RemoveAt(i);
        }

        int pixels = (int)Math.Round(View.IconSize * _scale);
        int overlayPixels = (int)Math.Round(Math.Min(View.IconSize, DesktopIcon.MaxOverlaySize) * _scale);
        if (_overlay.Pixels != overlayPixels)
            LoadOverlay(overlayPixels);

        for (int i = 0; i < entries.Count; i++)
        {
            DesktopEntry entry = entries[i];
            if (Find(entry.Item.ParsingName) is { } icon)
            {
                icon.Entry = entry;
                int at = Icons.IndexOf(icon);
                if (at != i)
                    Icons.Move(at, i);
            }
            else
            {
                icon = new DesktopIcon(entry);
                Icons.Insert(i, icon);
            }

            icon.Size = View.IconSize;
            icon.Overlay = entry.Item.IsLink ? _overlay.Image : null;
            DesktopItem item = entry.Item;
            AppIcons.Load(() => DesktopFolder.GetIcon(item, pixels), image =>
            {
                if (icon.Item == item)
                    icon.Image = image;
            });
        }
    }

    private void LoadOverlay(int pixels)
    {
        _overlay = (pixels, null);
        AppIcons.Load(() => DesktopFolder.GetShortcutOverlay(pixels), image =>
        {
            if (_overlay.Pixels != pixels)
                return;
            _overlay = (pixels, image);
            foreach (DesktopIcon icon in Icons.Where(icon => icon.Item.IsLink))
                icon.Overlay = image;
        });
    }

    private void Watch(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            // Raised on thread-pool threads.
            FileSystemEventHandler changed = (_, _) => _dispatcher.Post(QueueRefresh);
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Changed += changed;
            watcher.Renamed += (_, _) => _dispatcher.Post(QueueRefresh);
            watcher.Error += (_, e) =>
            {
                // Usually an overflowing buffer: changes were missed, so read everything again.
                Log.Warn($"Watching {folder} failed", e.GetException());
                _dispatcher.Post(QueueRefresh);
            };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Could not watch {folder}", ex);
        }
    }
}
