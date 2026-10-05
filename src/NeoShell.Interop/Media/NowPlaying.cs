using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace NeoShell.Interop.Media;

/// <summary>What the current media session plays, as Windows' media flyout shows it.</summary>
/// <param name="Thumbnail">The album art or video frame as an encoded image (PNG, JPEG), when the app gives one.</param>
public sealed record MediaInfo(
    string Title,
    string Artist,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanGoPrevious,
    bool CanGoNext,
    byte[]? Thumbnail);

/// <summary>
/// The media session Windows considers current (music players, browsers…), through the system media transport
/// controls. <see cref="Changed"/> comes from a thread-pool thread.
/// </summary>
public sealed class NowPlaying : IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private NowPlaying(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        Watch(_manager.GetCurrentSession());
    }

    /// <summary>Null where the media controls aren't available.</summary>
    public static async Task<NowPlaying?> CreateAsync()
    {
        try
        {
            return new NowPlaying(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Another session became current, or the current one changed track or started or stopped playing.</summary>
    public event Action? Changed;

    /// <summary>The current session's media; null when nothing is playing or paused.</summary>
    public async Task<MediaInfo?> ReadAsync()
    {
        if (_session is not { } session)
            return null;

        try
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties properties = await session.TryGetMediaPropertiesAsync();
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = session.GetPlaybackInfo();
            GlobalSystemMediaTransportControlsSessionPlaybackControls controls = playback.Controls;
            return new MediaInfo(
                properties.Title,
                properties.Artist.Length > 0 ? properties.Artist : properties.AlbumArtist,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                await ReadThumbnailAsync(properties));
        }
        catch (COMException)
        {
            // The app closed its session meanwhile; its end comes as a change.
            return null;
        }
    }

    public Task PlayPauseAsync() => Send(session => session.TryTogglePlayPauseAsync().AsTask());

    public Task PreviousAsync() => Send(session => session.TrySkipPreviousAsync().AsTask());

    public Task NextAsync() => Send(session => session.TrySkipNextAsync().AsTask());

    public void Dispose()
    {
        _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        Watch(null);
    }

    private async Task Send(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (_session is not { } session)
            return;

        try
        {
            await command(session);
        }
        catch (COMException)
        {
            // Gone meanwhile.
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (properties.Thumbnail is null)
            return null;

        using var stream = (await properties.Thumbnail.OpenReadAsync()).AsStreamForRead();
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        return bytes.ToArray();
    }

    private void Watch(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
        }
        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        Watch(sender.GetCurrentSession());
        Changed?.Invoke();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => Changed?.Invoke();
}
