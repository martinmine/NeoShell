using System.Collections.ObjectModel;
using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Desktop;

/// <summary>
/// What the desktop's icons show (shell mode only; Explorer has its own otherwise): the desktop's items, kept up to
/// date as files come and go, the view settings, and where each icon is. Like Explorer's desktop, one desktop spans
/// every monitor: each monitor's <see cref="DesktopIconsView"/> shows the icons on it, and they share one selection.
/// Places are kept in Explorer's own saved layouts (<see cref="IconLayouts"/>), so they carry over between shells.
/// </summary>
internal sealed class DesktopIcons : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SettingsStore _settings;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<nint, ObservableCollection<DesktopIcon>> _monitorIcons = [];
    private readonly Dictionary<int, ImageSource?> _overlays = [];
    /// <summary>Explorer's saved layouts; null when they couldn't be read, and then they're never written over.</summary>
    private IconLayouts? _layouts = IconLayouts.Read();
    private Dictionary<(string Name, LayoutIconFlags Flags), IconPlace> _saved = [];
    private IReadOnlyList<DesktopWorkspace> _workspaces = [];
    private IReadOnlyList<DisplayMonitor> _monitors = [];
    private string? _key;
    private int _version;
    private bool _disposed;
    private bool _reloadImages;
    private bool _pack;
    private NewItemsPlace? _newItemsAt;
    private readonly Dictionary<string, IconPlace> _renamed = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public DesktopIcons(SettingsStore settings)
    {
        _settings = settings;
        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(200);
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Save();

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

    /// <summary>The icons got new places (and maybe new monitors): the views lay them out again.</summary>
    public event Action? Arranged;

    /// <summary>The shared selection changed outside a view; each view shows it for its icons.</summary>
    public event Action? SelectionChanged;

    /// <summary>A selection rectangle being dragged, in screen pixels (null when it's let go), and what Ctrl kept selected.</summary>
    public event Action<RectInt32?, IReadOnlySet<DesktopIcon>>? MarqueeChanged;

    /// <summary>The keyboard moved to this icon: the view showing it takes the focus.</summary>
    public event Action<DesktopIcon>? FocusRequested;

    /// <summary>A drag of the desktop's own icons started (<see cref="OwnDrag"/> set) or ended (null).</summary>
    public event Action? OwnDragChanged;

    /// <summary>Every icon, in sort order.</summary>
    public ObservableCollection<DesktopIcon> Icons { get; } = [];

    public DesktopViewSettings View { get; private set; } = DesktopViewSettings.Read();

    public DesktopSortOrder SortOrder => _settings.Current.DesktopSortOrder;

    /// <summary>The monitors' workspaces in Explorer's order: what <see cref="IconPlace.Workspace"/> counts.</summary>
    public IReadOnlyList<DesktopWorkspace> Workspaces => _workspaces;

    /// <summary>The icons being dragged by the desktop itself, or null.</summary>
    public OwnDrag? OwnDrag { get; private set; }

    public IEnumerable<DesktopIcon> Selection => Icons.Where(icon => icon.IsSelected);

    /// <summary>The icons on one monitor, in sort order: what its view shows.</summary>
    public ObservableCollection<DesktopIcon> IconsOn(nint monitor)
    {
        if (!_monitorIcons.TryGetValue(monitor, out ObservableCollection<DesktopIcon>? icons))
            _monitorIcons[monitor] = icons = [.. Icons.Where(icon => MonitorOf(icon) == monitor)];
        return icons;
    }

    /// <summary>The workspace showing a monitor, or null.</summary>
    public DesktopWorkspace? WorkspaceOf(nint monitor) =>
        MonitorIndex(monitor) is int index and >= 0 ? _workspaces[index] : null;

    /// <summary>The workspace an icon is on.</summary>
    public DesktopWorkspace? WorkspaceOf(DesktopIcon icon) =>
        icon.Place is { } place && place.Workspace < _workspaces.Count ? _workspaces[place.Workspace] : null;

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
            Update(DesktopContents.Sort(entries, SortOrder), locations);
            _loaded = true;
            _reloadImages = true;
            Arrange();
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

    /// <summary>Sorts the icons: each monitor's are packed again in that order from its top left, as in Explorer.</summary>
    public void SetSortOrder(DesktopSortOrder order)
    {
        _settings.Update(_settings.Current with { DesktopSortOrder = order });
        _pack = true;
        _ = RefreshAsync();
    }

    /// <summary>"Auto arrange icons": each monitor's icons stay packed in sort order, still on their own monitor.</summary>
    public void SetAutoArrange(bool autoArrange)
    {
        DesktopViewSettings.SaveAutoArrange(autoArrange);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gives every icon its place for the monitors there are now: where it was left (in this arrangement's saved
    /// layout, or the one that fits best when the arrangement is new), packed while icons are auto-arranged, the
    /// first free cells for icons new to the desktop, or where they were dropped (<see cref="ExpectNewItemsAt"/>).
    /// </summary>
    public void Arrange()
    {
        // Before the desktop is first read there's nothing to place, and nothing to save over the places kept.
        if (!_loaded)
            return;

        IReadOnlyList<DisplayMonitor> monitors = [.. DisplayMonitor.GetAll().OrderBy(monitor => monitor.WorkArea.X).ThenBy(monitor => monitor.WorkArea.Y)];
        IReadOnlyList<DesktopWorkspace> workspaces = DesktopLayout.Workspaces(
            monitors.Select(monitor => (monitor.WorkArea, monitor.Dpi, monitor.IsPrimary)), View.IconSize);
        if (workspaces.Count == 0)
            return;
        string key = DesktopLayout.Key(workspaces);
        if (key != _key)
        {
            // Another monitor arrangement, icon size or work area: its own saved places, as in Explorer. The places
            // of the one before are saved for it first.
            if (_saveTimer.IsRunning)
                Save();
            _key = key;
            _saved = _layouts is null ? [] : DesktopLayout.SavedPlaces(_layouts, workspaces);
            foreach (DesktopIcon icon in Icons)
                icon.Place = null;
            Log.Info($"Desktop icons: {key}, {_saved.Count} saved place(s)");
        }
        _monitors = monitors;
        _workspaces = workspaces;

        IReadOnlyList<GridSize> grids = [.. _workspaces.Select(workspace => workspace.Grid)];
        int primary = Math.Max(0, _workspaces.ToList().FindIndex(workspace => workspace.IsPrimary));
        IconPlace?[] wanted = [.. Icons.Select(Wanted)];
        IconPlace[] places = View.AutoArrange || _pack
            ? DesktopGrid.Pack([.. wanted.Select(place => place?.Workspace)], grids, primary)
            : DesktopGrid.Arrange(wanted, grids, primary);
        _pack = false;
        _renamed.Clear();

        if (_newItemsAt is { } newPlace && !View.AutoArrange)
        {
            List<(int, IconPlace)> moves = [.. Icons
                .Select((icon, index) => (icon, index))
                .Where(pair => !newPlace.Existing.Contains(pair.icon.Item.ParsingName))
                .Select(pair => (pair.index, newPlace.Place))];
            if (moves.Count > 0 || DateTime.UtcNow > newPlace.Until)
                _newItemsAt = null;
            places = DesktopGrid.Move(places, moves, grids);
        }

        Apply(places);
    }

    /// <summary>
    /// Moves icons by a distance on the screen (physical pixels), as when they're dragged: each to the cell nearest to
    /// where it lands, on the monitor it lands on (or the nearest one). Auto-arranged icons only change monitor.
    /// </summary>
    public void MoveBy(IReadOnlyList<DesktopIcon> icons, int dx, int dy)
    {
        if (_workspaces.Count == 0)
            return;

        IReadOnlyList<GridSize> grids = [.. _workspaces.Select(workspace => workspace.Grid)];
        var moves = new List<(int, IconPlace)>();
        foreach (DesktopIcon icon in icons)
        {
            if (Icons.IndexOf(icon) is int index and >= 0 && WorkspaceOf(icon) is { } from)
            {
                PointInt32 origin = from.CellOrigin(icon.Place!.Value.Cell);
                var landed = new PointInt32(origin.X + dx, origin.Y + dy);
                var center = new PointInt32(landed.X + from.CellWidth / 2, landed.Y + from.CellHeight / 2);
                int workspace = Enumerable.Range(0, _workspaces.Count).MinBy(i => _workspaces[i].DistanceSquared(center));
                moves.Add((index, new IconPlace(workspace, _workspaces[workspace].NearestCell(landed))));
            }
        }

        IconPlace[] places = [.. Icons.Select(icon => icon.Place ?? new IconPlace(0, new GridCell(0, 0)))];
        if (View.AutoArrange)
        {
            int?[] workspaces = [.. places.Select(place => (int?)place.Workspace)];
            foreach ((int index, IconPlace target) in moves)
                workspaces[index] = target.Workspace;
            int primary = Math.Max(0, _workspaces.ToList().FindIndex(workspace => workspace.IsPrimary));
            Apply(DesktopGrid.Pack(workspaces, grids, primary));
        }
        else
        {
            Apply(DesktopGrid.Move(places, moves, grids));
        }
    }

    /// <summary>The place on the desktop at a point on the screen (physical pixels): the cell under it.</summary>
    public IconPlace? PlaceAt(int x, int y)
    {
        var point = new PointInt32(x, y);
        for (int i = 0; i < _workspaces.Count; i++)
        {
            DesktopWorkspace workspace = _workspaces[i];
            if (workspace.DistanceSquared(point) == 0)
                return new IconPlace(i, new GridCell((x - workspace.WorkArea.X) / workspace.CellWidth, (y - workspace.WorkArea.Y) / workspace.CellHeight));
        }
        return null;
    }

    /// <summary>
    /// The next items to appear on the desktop go to this place (or the nearest free ones), as files dropped on the
    /// desktop or made with New do in Explorer. The shell may take a while to copy them; after a minute it's forgotten.
    /// </summary>
    public void ExpectNewItemsAt(IconPlace place) =>
        _newItemsAt = new NewItemsPlace(
            place,
            Icons.Select(icon => icon.Item.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow.AddMinutes(1));

    /// <summary>A renamed item keeps its place.</summary>
    public void Renamed(string oldParsingName, string newParsingName)
    {
        if (Find(oldParsingName)?.Place is { } place)
            _renamed[newParsingName] = place;
    }

    // The selection is shared by every monitor's view, as in Explorer's one desktop window.

    /// <summary>Selects exactly these icons.</summary>
    public void SelectOnly(IEnumerable<DesktopIcon> icons)
    {
        var selected = icons.ToHashSet();
        foreach (DesktopIcon icon in Icons)
            icon.IsSelected = selected.Contains(icon);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection() => SelectOnly([]);

    public void SelectAll() => SelectOnly(Icons);

    /// <summary>Shows a selection rectangle on every monitor it crosses; each view selects the icons it touches.</summary>
    public void SetMarquee(RectInt32? rect, IReadOnlySet<DesktopIcon> kept) => MarqueeChanged?.Invoke(rect, kept);

    /// <summary>
    /// Selects the nearest icon in a direction from the focused one, across monitors, keeping to its row or column
    /// where it can (icons sit anywhere, so a list's arrow keys, which go by order, won't do). The view showing it
    /// takes the focus.
    /// </summary>
    public void SelectNext(DesktopIcon? from, int columns, int rows)
    {
        DesktopIcon? next;
        if (from is null || Center(from) is not { } start)
        {
            next = Icons.Where(icon => icon.Place is not null)
                .OrderBy(icon => icon.Place!.Value.Workspace).ThenBy(icon => icon.Cell.Column).ThenBy(icon => icon.Cell.Row)
                .FirstOrDefault();
        }
        else
        {
            next = Icons
                .Select(icon => (icon, center: Center(icon)))
                .Where(pair => pair.icon != from && pair.center is not null)
                .Select(pair => (pair.icon, dx: (long)pair.center!.Value.X - start.X, dy: (long)pair.center!.Value.Y - start.Y))
                .Where(pair => columns != 0 ? Math.Sign(pair.dx) == columns : Math.Sign(pair.dy) == rows)
                .OrderBy(pair =>
                {
                    long along = columns != 0 ? pair.dx : pair.dy;
                    long across = columns != 0 ? pair.dy : pair.dx;
                    return along * along + 4 * across * across;
                })
                .Select(pair => pair.icon)
                .FirstOrDefault();
        }
        if (next is null)
            return;

        SelectOnly([next]);
        FocusRequested?.Invoke(next);
    }

    /// <summary>Starts a drag of the desktop's own icons, which any monitor's view may take.</summary>
    public void BeginOwnDrag(OwnDrag drag)
    {
        OwnDrag = drag;
        OwnDragChanged?.Invoke();
    }

    public void EndOwnDrag()
    {
        OwnDrag = null;
        OwnDragChanged?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        if (_saveTimer.IsRunning)
            Save();
        _saveTimer.Stop();
        foreach (FileSystemWatcher watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }

    private IconPlace? Wanted(DesktopIcon icon) =>
        icon.Place
        ?? (_renamed.TryGetValue(icon.Item.ParsingName, out IconPlace renamed) ? renamed
            : _saved.TryGetValue((icon.LayoutName, icon.LayoutFlags), out IconPlace saved) ? saved
            : null);

    /// <summary>Gives the icons their places, moves them between the monitors' lists, and saves the places.</summary>
    private void Apply(IReadOnlyList<IconPlace> places)
    {
        bool changed = false;
        for (int i = 0; i < Icons.Count; i++)
        {
            changed |= Icons[i].Place != places[i];
            Icons[i].Place = places[i];
        }
        SyncMonitorLists();
        LoadImages();
        Arranged?.Invoke();
        if (changed)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    /// <summary>Brings each monitor's list in line with where the icons are, in sort order, keeping icons that stay.</summary>
    private void SyncMonitorLists()
    {
        // The lists of monitors that are gone (their windows were closed) aren't kept up.
        foreach (nint gone in _monitorIcons.Keys.Where(monitor => MonitorIndex(monitor) < 0).ToList())
            _monitorIcons.Remove(gone);
        foreach ((nint monitor, ObservableCollection<DesktopIcon> list) in _monitorIcons)
        {
            List<DesktopIcon> wanted = [.. Icons.Where(icon => MonitorOf(icon) == monitor)];
            var keep = wanted.ToHashSet();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(list[i]))
                    list.RemoveAt(i);
            }
            for (int i = 0; i < wanted.Count; i++)
            {
                int at = list.IndexOf(wanted[i]);
                if (at == i)
                    continue;
                if (at >= 0)
                    list.Move(at, i);
                else
                    list.Insert(i, wanted[i]);
            }
        }
    }

    private nint MonitorOf(DesktopIcon icon) =>
        icon.Place is { } place && place.Workspace < _monitors.Count ? _monitors[place.Workspace].Handle : 0;

    private int MonitorIndex(nint monitor)
    {
        for (int i = 0; i < _monitors.Count; i++)
        {
            if (_monitors[i].Handle == monitor)
                return i;
        }
        return -1;
    }

    /// <summary>The middle of an icon's cell on the screen.</summary>
    private PointInt32? Center(DesktopIcon icon)
    {
        if (WorkspaceOf(icon) is not { } workspace)
            return null;
        PointInt32 origin = workspace.CellOrigin(icon.Cell);
        return new PointInt32(origin.X + workspace.CellWidth / 2, origin.Y + workspace.CellHeight / 2);
    }

    /// <summary>Writes every icon's place into Explorer's saved layout for these monitors.</summary>
    private void Save()
    {
        _saveTimer.Stop();
        if (_layouts is null || _workspaces.Count == 0 || !_loaded)
            return;
        _layouts = DesktopLayout.WithPlaces(
            _layouts,
            _workspaces,
            Icons.Where(icon => icon.Place is not null).Select(icon => (icon.LayoutName, icon.LayoutFlags, icon.Place!.Value)));
        _layouts.Save();
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
    private void Update(IReadOnlyList<DesktopEntry> entries, DesktopLocations locations)
    {
        var wanted = entries.Select(entry => entry.Item.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Icons.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Icons[i].Item.ParsingName))
                Icons.RemoveAt(i);
        }

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
            (icon.LayoutName, icon.LayoutFlags) = DesktopContents.LayoutName(entry.Item, locations);
        }
    }

    /// <summary>
    /// Loads each icon's image at its monitor's pixel size: again after a refresh (images show state) or when the
    /// icon moved to a monitor of another DPI.
    /// </summary>
    private void LoadImages()
    {
        bool all = _reloadImages;
        _reloadImages = false;
        foreach (DesktopIcon icon in Icons)
        {
            if (WorkspaceOf(icon) is not { } workspace)
                continue;
            double scale = workspace.Dpi / 96.0;
            int pixels = (int)Math.Round(View.IconSize * scale);
            if (!all && icon.ImagePixels == pixels)
                continue;
            icon.ImagePixels = pixels;
            DesktopItem item = icon.Item;
            AppIcons.Load(() => DesktopFolder.GetIcon(item, pixels), image =>
            {
                if (icon.Item == item && icon.ImagePixels == pixels)
                    icon.Image = image;
            });
            if (item.IsLink)
                SetOverlay(icon, (int)Math.Round(Math.Min(View.IconSize, DesktopIcon.MaxOverlaySize) * scale));
            else
                icon.Overlay = null;
        }
    }

    /// <summary>The stock shortcut arrow at a pixel size, loaded once per size.</summary>
    private void SetOverlay(DesktopIcon icon, int pixels)
    {
        icon.OverlayPixels = pixels;
        if (_overlays.TryGetValue(pixels, out ImageSource? overlay))
        {
            icon.Overlay = overlay; // null while it loads
            return;
        }
        _overlays[pixels] = null;
        AppIcons.Load(() => DesktopFolder.GetShortcutOverlay(pixels), image =>
        {
            _overlays[pixels] = image;
            foreach (DesktopIcon other in Icons.Where(other => other.Item.IsLink && other.OverlayPixels == pixels))
                other.Overlay = image;
        });
    }

    /// <summary>Where the next new items go, and the items there before.</summary>
    private sealed record NewItemsPlace(IconPlace Place, HashSet<string> Existing, DateTime Until);

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

/// <summary>
/// The desktop's own icons being dragged, the one under the pointer, and where the pointer was from that icon's cell
/// (screen pixels): let go, they move by as much as the pointer did, as in Explorer.
/// </summary>
internal sealed record OwnDrag(IReadOnlyList<DesktopIcon> Icons, DesktopIcon Grabbed, PointInt32 Start);
