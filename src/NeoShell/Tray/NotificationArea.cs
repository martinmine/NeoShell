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
/// <see cref="TrayHost"/>, keeps the icons, and passes clicks back to the apps.
/// </summary>
internal sealed class NotificationArea : IDisposable
{
    private static readonly TimeSpan s_cleanupInterval = TimeSpan.FromSeconds(5);

    private readonly TrayIconStore _store = new();
    private readonly Dictionary<string, ImageSource?> _images = [];
    private readonly TrayHost _host;
    private readonly DispatcherQueueTimer _cleanup;

    public NotificationArea()
    {
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

    /// <summary>Publishes an edge's auto-hide bar for shell32 (see <see cref="TrayHost.PublishAutoHideBar"/>).</summary>
    public void PublishAutoHideBar(nint taskbarMonitor, nint monitor, AppBarEdge edge, nint bar) =>
        _host.PublishAutoHideBar(taskbarMonitor, monitor, edge, bar);

    /// <summary>The icons that aren't hidden, in the order they were added.</summary>
    public ObservableCollection<TrayIcon> Icons { get; } = [];

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
        _cleanup.Stop();
        _host.Dispose();
    }

    private bool OnCommand(NotifyIconData data)
    {
        TrayIconState? before = _store.Find(data.Window, data.Id, data.Guid);
        if (!_store.Apply(data))
            return false;

        TrayIconState? icon = _store.Find(data.Window, data.Id, data.Guid);
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

    private void OnBalloon(TrayIconState icon, NotifyIconData data)
    {
        if (data.Info.Length == 0)
        {
            BalloonWithdrawn?.Invoke(icon.Key);
            return;
        }

        // Read now, while the app waits: it may destroy its icons as soon as this call returns.
        TrayBalloon balloon = CreateBalloon(icon, data);
        NotifyIconInput.Send(icon.Window, icon.Id, icon.CallbackMessage, icon.Version, BalloonEvent.Shown);
        Log.Info($"Balloon from {balloon.AppName} ({balloon.AppId})");
        BalloonRequested?.Invoke(balloon);
    }

    // The picture as Explorer loads it, at 256 pixels (the toast scales it down), and the header's logo for 16
    // effective pixels, sharp up to 200 %.
    private static TrayBalloon CreateBalloon(TrayIconState icon, NotifyIconData data)
    {
        WindowInfo window = WindowInfo.Read(icon.Window);
        string path = window.ProcessPath ?? "";
        string? settingsId = path.Length > 0 ? NotifyIconSettings.FindId(path, icon.Id, icon.Guid) : null;
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
        TrayIcon? icon = state is null ? null : Icons.FirstOrDefault(i => i.Key == state.Key);
        return icon is null ? null : IconBounds?.Invoke(icon);
    }

    private void RemoveDeadIcons()
    {
        if (_store.RemoveDeadOwners(TopLevelWindows.Exists))
            Refresh();
    }

    /// <summary>Brings <see cref="Icons"/> in line with the store, updating icons in place.</summary>
    private void Refresh()
    {
        List<TrayIconState> visible = [.. _store.Icons.Where(icon => !icon.IsHidden && !icon.IsSystemIcon)];
        // Gone icons first, so the icons after them aren't each moved up a place.
        var keys = visible.Select(state => state.Key).ToHashSet();
        for (int i = Icons.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(Icons[i].Key))
                Icons.RemoveAt(i);
        }
        for (int i = 0; i < visible.Count; i++)
        {
            TrayIconState state = visible[i];
            int existing = -1;
            for (int j = i; j < Icons.Count; j++)
            {
                if (Icons[j].Key == state.Key)
                {
                    existing = j;
                    break;
                }
            }

            if (existing < 0)
                Icons.Insert(i, new TrayIcon(state.Key));
            else if (existing != i)
                Icons.Move(existing, i);
            Icons[i].Update(state, _images.GetValueOrDefault(state.Key));
        }

        foreach (string gone in _images.Keys.Except(_store.Icons.Select(icon => icon.Key)).ToList())
            _images.Remove(gone);
    }
}
