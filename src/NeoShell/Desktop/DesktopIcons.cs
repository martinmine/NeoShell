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
    private NewItemsPlace? _newItemsAt;
    private bool _loaded;

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
            _loaded = true;
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

    /// <summary>Sorts the icons, which packs them again in that order from the top left, as in Explorer.</summary>
    public void SetSortOrder(DesktopSortOrder order)
    {
        _settings.Update(_settings.Current with { DesktopSortOrder = order, DesktopIconPositions = ShellSettings.NoIconPositions });
        _ = RefreshAsync();
    }

    /// <summary>
    /// "Auto arrange icons". Turned on, the places the user gave icons are forgotten: turned off again, the icons stay
    /// as they were arranged.
    /// </summary>
    public void SetAutoArrange(bool autoArrange)
    {
        DesktopViewSettings.SaveAutoArrange(autoArrange);
        if (autoArrange)
            _settings.Update(_settings.Current with { DesktopIconPositions = ShellSettings.NoIconPositions });
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gives every icon its cell on a grid of the given size (the work area's): where it was put, as Explorer
    /// remembers it, unless icons are auto-arranged. Icons new to the desktop fill the first gaps, or go where they
    /// were dropped (<see cref="ExpectNewItemsAt"/>).
    /// </summary>
    /// <param name="rows">How many icons fit in a column.</param>
    /// <param name="columns">How many columns fit on the screen.</param>
    public void Arrange(int rows, int columns)
    {
        // Before the desktop is first read there's nothing to place, and nothing to save over the places kept.
        if (!_loaded)
            return;

        bool free = !View.AutoArrange;
        var saved = new Dictionary<string, GridCell>(_settings.Current.DesktopIconPositions, StringComparer.OrdinalIgnoreCase);
        GridCell[] cells = DesktopGrid.Arrange(
            [.. Icons.Select(icon => free && saved.TryGetValue(icon.Item.ParsingName, out GridCell cell) ? cell : (GridCell?)null)],
            rows);

        if (_newItemsAt is { } place && free)
        {
            List<(int, GridCell)> moves = [.. Icons
                .Select((icon, index) => (icon, index))
                .Where(pair => !place.Existing.Contains(pair.icon.Item.ParsingName))
                .Select(pair => (pair.index, place.Cell))];
            if (moves.Count > 0 || DateTime.UtcNow > place.Until)
                _newItemsAt = null;
            cells = DesktopGrid.Move(cells, moves, rows, columns);
        }

        for (int i = 0; i < Icons.Count; i++)
            Icons[i].Cell = cells[i];
        if (free)
            SavePositions();
    }

    /// <summary>Moves icons by whole cells, as when they're dragged across the desktop. Not while icons are auto-arranged.</summary>
    public void Move(IReadOnlyList<DesktopIcon> icons, int columnOffset, int rowOffset, int rows, int columns)
    {
        if (View.AutoArrange)
            return;

        List<(int, GridCell)> moves = [.. icons
            .Select(icon => (Icons.IndexOf(icon), new GridCell(icon.Cell.Column + columnOffset, icon.Cell.Row + rowOffset)))
            .Where(move => move.Item1 >= 0)];
        GridCell[] cells = DesktopGrid.Move([.. Icons.Select(icon => icon.Cell)], moves, rows, columns);
        for (int i = 0; i < Icons.Count; i++)
            Icons[i].Cell = cells[i];
        SavePositions();
    }

    /// <summary>
    /// The next items to appear on the desktop go to this cell (or the nearest free ones), as files dropped on the
    /// desktop or made with New do in Explorer. The shell may take a while to copy them; after a minute it's forgotten.
    /// </summary>
    public void ExpectNewItemsAt(GridCell cell) =>
        _newItemsAt = new NewItemsPlace(
            cell,
            Icons.Select(icon => icon.Item.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow.AddMinutes(1));

    /// <summary>A renamed item keeps its place.</summary>
    public void Renamed(string oldParsingName, string newParsingName)
    {
        var positions = new Dictionary<string, GridCell>(_settings.Current.DesktopIconPositions, StringComparer.OrdinalIgnoreCase);
        if (!positions.Remove(oldParsingName, out GridCell cell))
            return;
        positions[newParsingName] = cell;
        _settings.Update(_settings.Current with { DesktopIconPositions = positions });
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        foreach (FileSystemWatcher watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }

    /// <summary>Remembers where every icon is, when that changed.</summary>
    private void SavePositions()
    {
        IReadOnlyDictionary<string, GridCell> saved = _settings.Current.DesktopIconPositions;
        if (saved.Count == Icons.Count && Icons.All(icon => saved.TryGetValue(icon.Item.ParsingName, out GridCell cell) && cell == icon.Cell))
            return;
        _settings.Update(_settings.Current with { DesktopIconPositions = Icons.ToDictionary(icon => icon.Item.ParsingName, icon => icon.Cell) });
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

    /// <summary>Where the next new items go, and the items there before.</summary>
    private sealed record NewItemsPlace(GridCell Cell, HashSet<string> Existing, DateTime Until);

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
