using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Media;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>What's playing (music players, browsers…), with its art and previous, play/pause and next.</summary>
internal sealed partial class MediaWidget : WidgetView
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private NowPlaying? _nowPlaying;
    private bool _closed;
    // Reads overlap when changes come quickly; only the latest is shown.
    private int _read;

    public MediaWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        Start();
    }

    private bool ShowArtwork => Settings.ShowArtwork ?? true;

    public override void Close()
    {
        _closed = true;
        if (_nowPlaying is not null)
        {
            _nowPlaying.Changed -= OnChanged;
            _nowPlaying.Dispose();
        }
    }

    public override FrameworkElement CreateSettings()
    {
        var artwork = new ToggleSwitch { Header = "Show album art", IsOn = ShowArtwork };
        artwork.Toggled += (_, _) =>
        {
            SaveSettings(Settings with { ShowArtwork = artwork.IsOn });
            Refresh();
        };
        return artwork;
    }

    private async void Start()
    {
        NowPlaying? nowPlaying = await NowPlaying.CreateAsync();
        if (_closed)
        {
            nowPlaying?.Dispose();
            return;
        }
        _nowPlaying = nowPlaying;
        if (_nowPlaying is null)
        {
            Log.Warn("The media controls aren't available");
            return;
        }
        _nowPlaying.Changed += OnChanged;
        Refresh();
    }

    // From a thread-pool thread.
    private void OnChanged() => _dispatcher.Post(Refresh);

    private async void Refresh()
    {
        if (_nowPlaying is null || _closed)
            return;

        int read = ++_read;
        MediaInfo? media = await _nowPlaying.ReadAsync();
        if (read != _read || _closed)
            return;

        TitleText.Text = media?.Title is { Length: > 0 } title ? title : "Nothing playing";
        ArtistText.Text = media?.Artist ?? "";
        PreviousButton.IsEnabled = media?.CanGoPrevious == true;
        NextButton.IsEnabled = media?.CanGoNext == true;
        PlayPauseButton.IsEnabled = media?.CanPlayPause == true;
        bool playing = media?.IsPlaying == true;
        PlayPauseGlyph.Glyph = playing ? "" : "";
        string playPause = playing ? "Pause" : "Play";
        AutomationProperties.SetName(PlayPauseButton, playPause);
        ToolTipService.SetToolTip(PlayPauseButton, playPause);

        Artwork.Visibility = ShowArtwork ? Visibility.Visible : Visibility.Collapsed;
        await ShowThumbnail(ShowArtwork ? media?.Thumbnail : null, read);
    }

    private async Task ShowThumbnail(byte[]? thumbnail, int read)
    {
        if (thumbnail is null)
        {
            ArtImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var image = new BitmapImage { DecodePixelWidth = 128 };
            using var stream = new MemoryStream(thumbnail);
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            if (read != _read)
                return;
            ArtImage.Background = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            ArtImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not show the media's art", ex);
        }
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await (_nowPlaying?.PreviousAsync() ?? Task.CompletedTask);

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e) => await (_nowPlaying?.PlayPauseAsync() ?? Task.CompletedTask);

    private async void NextButton_Click(object sender, RoutedEventArgs e) => await (_nowPlaying?.NextAsync() ?? Task.CompletedTask);
}
