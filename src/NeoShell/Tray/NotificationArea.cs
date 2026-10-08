using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell.Tray;

/// <summary>
/// The system tray, when NeoShell is the shell: receives apps' <c>Shell_NotifyIcon</c> calls through
/// <see cref="TrayHost"/>, keeps the icons, and passes clicks back to the apps. Each icon is on the taskbar or behind
/// the chevron as Explorer's settings for it say (<see cref="NotifyIconSettings"/>), which it keeps as Explorer does,
/// following changes made to them while it runs.
/// </summary>
internal sealed class NotificationArea : IDisposable
{
    private static readonly TimeSpan s_cleanupInterval = TimeSpan.FromSeconds(5);

    private readonly TrayIconStore _store = new();
    private readonly Dictionary<string, ImageSource?> _images = [];
    private readonly Dictionary<string, TrayIcon> _trayIcons = [];
    // Each icon's key in NotifyIconSettings, by the icon's own key.
    private readonly Dictionary<string, ulong> _settingsIds = [];
    private readonly TrayHost _host;
    private readonly DispatcherQueueTimer _cleanup;
    private readonly RegistryWatcher _settingsWatcher;
    private readonly RegistryWatcher _chevronWatcher;
    private readonly bool _promoteNew;
    private IReadOnlyList<NotifyIconKey> _keys;
    private IReadOnlyList<ulong> _order;
    private bool _chevronVisible;

    /// <param name="showAll">
    /// Shows every icon on the taskbar, once: those Explorer knows and those added in this session (NeoShell's former
    /// "Show all tray icons" setting, which Explorer's per-icon settings replace).
    /// </param>
    public NotificationArea(bool showAll)
    {
        NotifyIconSettings.CreateIfMissing();
        if (showAll)
        {
            foreach (NotifyIconKey key in NotifyIconSettings.ReadAll())
                NotifyIconSettings.SetPromoted(key.Id, true);
            _promoteNew = true;
        }
        _keys = NotifyIconSettings.ReadAll();
        _order = NotifyIconSettings.ReadOrder();
        _chevronVisible = NotifyIconSettings.ChevronVisible;
        // Settings (with Explorer running), or anything else, may change them; Explorer follows such changes at once.
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _settingsWatcher = new RegistryWatcher(NotifyIconSettings.KeyPath, subtree: true);
        _settingsWatcher.Changed += () => dispatcher.TryEnqueue(ReloadSettings);
        _chevronWatcher = new RegistryWatcher(NotifyIconSettings.TrayNotifyPath);
        _chevronWatcher.Changed += () => dispatcher.TryEnqueue(() => SetChevronVisible(NotifyIconSettings.ChevronVisible));

        _host = new TrayHost(OnCommand, OnRectRequest);
        // Apps that crash or are killed never delete their icons.
        _cleanup = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _cleanup.Interval = s_cleanupInterval;
        _cleanup.Tick += (_, _) => RemoveDeadIcons();
        _cleanup.Start();
        Log.Info("Tray started");
    }

    /// <summary>Apps' ITaskbarList3 calls (progress, overlay icons), which reach the shell through the tray window.</summary>
    public event Action<TaskbarListCall>? TaskbarListCalled
    {
        add => _host.TaskbarListCalled += value;
        remove => _host.TaskbarListCalled -= value;
    }

    /// <summary>Apps' thumbnail toolbars (ITaskbarList3), which reach the shell the same way.</summary>
    public event Action<ThumbBarCall>? ThumbBarCalled
    {
        add => _host.ThumbBarCalled += value;
        remove => _host.ThumbBarCalled -= value;
    }

    /// <summary>Serves other apps' <c>SHAppBarMessage</c> calls, which reach the shell through the tray window too.</summary>
    public Func<AppBarMessage, nint>? AppBarMessageHandler
    {
        set => _host.AppBarMessageHandler = value;
    }

    /// <summary>A work area was set and every window told (see <see cref="TrayHost.WorkAreaChanged"/>).</summary>
    public event Action? WorkAreaChanged
    {
        add => _host.WorkAreaChanged += value;
        remove => _host.WorkAreaChanged -= value;
    }

    /// <summary>A window's title bar was shaken (see <see cref="TrayHost.WindowShaken"/>).</summary>
    public event Action<nint>? WindowShaken
    {
        add => _host.WindowShaken += value;
        remove => _host.WindowShaken -= value;
    }

    /// <summary>Publishes an edge's auto-hide bar for shell32 (see <see cref="TrayHost.PublishAutoHideBar"/>).</summary>
    public void PublishAutoHideBar(nint taskbarMonitor, nint monitor, AppBarEdge edge, nint bar) =>
        _host.PublishAutoHideBar(taskbarMonitor, monitor, edge, bar);

    /// <summary>The icons shown on the taskbar, in their order.</summary>
    public ObservableCollection<TrayIcon> PromotedIcons { get; } = [];

    /// <summary>The icons behind the chevron, in their order.</summary>
    public ObservableCollection<TrayIcon> OverflowIcons { get; } = [];

    /// <summary>
    /// Whether the chevron and the overflow are there: Settings' "Hidden icon menu". Off, the icons that would be in
    /// the overflow show nowhere.
    /// </summary>
    public bool ChevronVisible
    {
        get => _chevronVisible;
        set
        {
            NotifyIconSettings.ChevronVisible = value;
            SetChevronVisible(value);
        }
    }

    public event Action? ChevronVisibilityChanged;

    /// <summary>
    /// Moves an icon dragged to the taskbar or the overflow, as Explorer does: promoted or not, before or after the
    /// icon it was dropped on. Onto the chevron it goes first in the overflow. Returns whether it moved (an icon
    /// dropped on itself stays).
    /// </summary>
    public bool Move(TrayIcon icon, TrayDropTarget target)
    {
        ObservableCollection<TrayIcon> row = target.InOverflow ? OverflowIcons : PromotedIcons;
        TrayIcon? onto = target.IsChevron ? row.FirstOrDefault(other => other != icon) : row[target.Index];
        if (onto == icon || !_settingsIds.TryGetValue(icon.Key, out ulong id))
            return false;

        NotifyIconSettings.SetPromoted(id, !target.InOverflow);
        // Into an empty row there's nothing to be placed next to; the order stays.
        if (onto is not null && _settingsIds.TryGetValue(onto.Key, out ulong ontoId))
        {
            _order = TrayIconOrder.Move(_order, id, ontoId, target.After);
            NotifyIconSettings.WriteOrder(_order);
        }
        _keys = NotifyIconSettings.ReadAll();
        Refresh();
        return true;
    }

    /// <summary>Where an icon is on screen; asked by apps through <c>Shell_NotifyIconGetRect</c>.</summary>
    public Func<TrayIcon, RectInt32?>? IconBounds { get; set; }

    public void SetTaskbarBounds(RectInt32 bounds) => _host.SetTaskbarBounds(bounds);

    public void Send(TrayIcon icon, TrayMouseEvent mouseEvent)
    {
        TrayIconState state = icon.State;
        if (!TopLevelWindows.Exists(state.Window))
        {
            RemoveDeadIcons();
            return;
        }
        NotifyIconInput.Send(state.Window, state.Id, state.CallbackMessage, state.Version, mouseEvent, Cursor.Position());
    }

    /// <summary>
    /// An icon's app asked for a balloon notification. Its app has been told it showed (<c>NIN_BALLOONSHOW</c>, sent
    /// at once as Explorer does); tell it what became of it with <see cref="Send(TrayBalloon, BalloonEvent)"/>.
    /// </summary>
    public event Action<TrayBalloon>? BalloonRequested;

    /// <summary>
    /// An icon's balloon was taken back (an empty text) or its icon deleted: it goes without a word to the app, as in
    /// Explorer. Gives the icon's key.
    /// </summary>
    public event Action<string>? BalloonWithdrawn;

    /// <summary>Tells a balloon's app what became of it, if its icon is still there.</summary>
    public void Send(TrayBalloon balloon, BalloonEvent balloonEvent)
    {
        if (_store.Icons.FirstOrDefault(icon => icon.Key == balloon.IconKey) is { } state && TopLevelWindows.Exists(state.Window))
            NotifyIconInput.Send(state.Window, state.Id, state.CallbackMessage, state.Version, balloonEvent);
    }

    public void Dispose()
    {
        _settingsWatcher.Dispose();
        _chevronWatcher.Dispose();
        _cleanup.Stop();
        _host.Dispose();
    }

    private bool OnCommand(NotifyIconData data)
    {
        TrayIconState? before = _store.Find(data.Window, data.Id, data.Guid);
        if (!_store.Apply(data))
            return false;

        TrayIconState? icon = _store.Find(data.Window, data.Id, data.Guid);
        // Explorer drops its own system icons before it ever looks for their settings.
        if (data.Command == NotifyIconCommand.Add && icon is { IsSystemIcon: false })
            FindSettings(icon);
        if (data.Flags.HasFlag(NotifyIconFlags.Icon) && icon is not null)
        {
            // Copied now: the app may destroy its icon as soon as this call returns.
            IconBitmap? pixels = icon.IconHandle != 0 ? IconBitmap.FromIcon(icon.IconHandle) : null;
            _images[icon.Key] = pixels is null ? null : AppIcons.ToImageSource(pixels);
        }
        if (data.Command == NotifyIconCommand.Delete && before is not null)
            BalloonWithdrawn?.Invoke(before.Key);
        else if (data.Flags.HasFlag(NotifyIconFlags.Info) && icon is not null)
            OnBalloon(icon, data);
        Refresh();
        return true;
    }

    // The icon's key in NotifyIconSettings, added for an icon seen for the first time, as Explorer does (Taskbar.dll's
    // NotificationAreaIconManager2::AddIcon), hidden ones too.
    private void FindSettings(TrayIconState icon)
    {
        if (WindowInfo.Read(icon.Window).ProcessPath is not { } path)
            return;
        // The shell service objects' icons (Safely Remove Hardware…) are the shell's: Explorer's, in its settings.
        if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (NotifyIconSettings.Find(_keys, path, icon.Id, icon.Guid) is { } key)
        {
            _settingsIds[icon.Key] = key.Id;
            return;
        }

        ulong id = NotifyIconSettings.Add(path, icon.Id, icon.Guid, icon.Tip, _order);
        if (_promoteNew)
            NotifyIconSettings.SetPromoted(id, true);
        _settingsIds[icon.Key] = id;
        _keys = NotifyIconSettings.ReadAll();
        _order = NotifyIconSettings.ReadOrder();
        Log.Info($"Tray icon from {Path.GetFileName(path)} added to NotifyIconSettings as {id}");
    }

    private void ReloadSettings()
    {
        _keys = NotifyIconSettings.ReadAll();
        _order = NotifyIconSettings.ReadOrder();
        Refresh();
    }

    private void SetChevronVisible(bool visible)
    {
        if (visible == _chevronVisible)
            return;
        _chevronVisible = visible;
        ChevronVisibilityChanged?.Invoke();
    }

    private void OnBalloon(TrayIconState icon, NotifyIconData data)
    {
        if (data.Info.Length == 0)
        {
            BalloonWithdrawn?.Invoke(icon.Key);
            return;
        }

        // Read now, while the app waits: it may destroy its icons as soon as this call returns.
        TrayBalloon balloon = CreateBalloon(icon, data, _settingsIds.TryGetValue(icon.Key, out ulong id) ? id.ToString() : null);
        NotifyIconInput.Send(icon.Window, icon.Id, icon.CallbackMessage, icon.Version, BalloonEvent.Shown);
        Log.Info($"Balloon from {balloon.AppName} ({balloon.AppId})");
        BalloonRequested?.Invoke(balloon);
    }

    // The picture as Explorer loads it, at 256 pixels (the toast scales it down), and the header's logo for 16
    // effective pixels, sharp up to 200 %.
    private static TrayBalloon CreateBalloon(TrayIconState icon, NotifyIconData data, string? settingsId)
    {
        WindowInfo window = WindowInfo.Read(icon.Window);
        string path = window.ProcessPath ?? "";
        string appId = TrayBalloon.AppIdFor(window.AppUserModelId, settingsId, JumpLists.ImplicitAppId(path));
        // An app of its own shows as Windows knows it; otherwise Explorer names it after the executable and takes the
        // tray icon for its logo.
        string? name = window.AppUserModelId is not null ? ShellItems.GetDisplayName(ShellItems.AppsFolderPath(appId)) : null;
        IconBitmap? logo = window.AppUserModelId is null ? BalloonIcons.Sharp(icon.IconHandle, 32) : null;
        (string title, string body) = TrayBalloon.Texts(data.InfoTitle, data.Info);
        return new TrayBalloon(
            icon.Key,
            appId,
            name ?? FileDescription(path) ?? Path.GetFileName(path),
            title,
            body,
            BalloonIcons.Load(data.InfoFlags, data.BalloonIcon, icon.IconHandle, 256),
            logo,
            data.InfoFlags.HasFlag(BalloonFlags.NoSound));
    }

    private static string? FileDescription(string path)
    {
        try
        {
            string? description = path.Length > 0 ? FileVersionInfo.GetVersionInfo(path).FileDescription : null;
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private RectInt32? OnRectRequest(NotifyIconRectRequest request)
    {
        TrayIconState? state = _store.Find(request.Window, request.Id, request.Guid);
        TrayIcon? icon = state is null ? null : _trayIcons.GetValueOrDefault(state.Key);
        return icon is null ? null : IconBounds?.Invoke(icon);
    }

    private void RemoveDeadIcons()
    {
        if (_store.RemoveDeadOwners(TopLevelWindows.Exists))
            Refresh();
    }

    /// <summary>Brings the two rows in line with the store and the settings, updating icons in place.</summary>
    private void Refresh()
    {
        var promoted = _keys.Where(key => key.IsPromoted).Select(key => key.Id).ToHashSet();
        List<TrayIconState> visible = TrayIconOrder.Sort(
            _store.Icons.Where(icon => !icon.IsHidden && !icon.IsSystemIcon),
            icon => _settingsIds.TryGetValue(icon.Key, out ulong id) ? id : null,
            _order);
        foreach (TrayIconState state in visible)
        {
            if (!_trayIcons.TryGetValue(state.Key, out TrayIcon? icon))
                _trayIcons[state.Key] = icon = new TrayIcon(state.Key);
            icon.Update(state, _images.GetValueOrDefault(state.Key));
        }
        bool IsPromoted(TrayIconState state) => _settingsIds.TryGetValue(state.Key, out ulong id) && promoted.Contains(id);
        Sync(PromotedIcons, [.. visible.Where(IsPromoted).Select(state => _trayIcons[state.Key])]);
        Sync(OverflowIcons, [.. visible.Where(state => !IsPromoted(state)).Select(state => _trayIcons[state.Key])]);

        var stored = _store.Icons.Select(icon => icon.Key).ToHashSet();
        var shown = visible.Select(icon => icon.Key).ToHashSet();
        foreach (string gone in _images.Keys.Where(key => !stored.Contains(key)).ToList())
            _images.Remove(gone);
        foreach (string gone in _settingsIds.Keys.Where(key => !stored.Contains(key)).ToList())
            _settingsIds.Remove(gone);
        foreach (string gone in _trayIcons.Keys.Where(key => !shown.Contains(key)).ToList())
            _trayIcons.Remove(gone);
    }

    // Moves rather than replaces, so the icons that stay keep their containers.
    private static void Sync(ObservableCollection<TrayIcon> row, List<TrayIcon> icons)
    {
        // Gone icons first, so the icons after them aren't each moved up a place.
        for (int i = row.Count - 1; i >= 0; i--)
        {
            if (!icons.Contains(row[i]))
                row.RemoveAt(i);
        }
        for (int i = 0; i < icons.Count; i++)
        {
            int existing = row.IndexOf(icons[i]);
            if (existing < 0)
                row.Insert(i, icons[i]);
            else if (existing != i)
                row.Move(existing, i);
        }
    }
}
