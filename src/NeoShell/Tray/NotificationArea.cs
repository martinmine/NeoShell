using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Imaging;
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

    public void Dispose()
    {
        _cleanup.Stop();
        _host.Dispose();
    }

    private bool OnCommand(NotifyIconData data)
    {
        if (!_store.Apply(data))
            return false;

        if (data.Flags.HasFlag(NotifyIconFlags.Icon) && _store.Find(data.Window, data.Id, data.Guid) is { } icon)
        {
            // Copied now: the app may destroy its icon as soon as this call returns.
            IconBitmap? pixels = icon.IconHandle != 0 ? IconBitmap.FromIcon(icon.IconHandle) : null;
            _images[icon.Key] = pixels is null ? null : AppIcons.ToImageSource(pixels);
        }
        Refresh();
        return true;
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
        List<TrayIconState> visible = [.. _store.Icons.Where(icon => !icon.IsHidden)];
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
        while (Icons.Count > visible.Count)
            Icons.RemoveAt(Icons.Count - 1);

        foreach (string gone in _images.Keys.Except(_store.Icons.Select(icon => icon.Key)).ToList())
            _images.Remove(gone);
    }
}
