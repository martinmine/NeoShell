using Microsoft.UI.Dispatching;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;

namespace NeoShell.Desktop;

/// <summary>
/// Advances the desktop slideshow while NeoShell is the shell, as Explorer's desktop does: on Explorer's schedule
/// (<see cref="SlideshowSettings.NextChange"/>), a different picture per monitor, in Explorer's order, each change
/// written back as Explorer's per-monitor values so Explorer would continue from it.
/// </summary>
/// <param name="changed">After pictures were changed, with the crossfade's duration in milliseconds.</param>
internal sealed class Slideshow(Action<int> changed) : IDisposable
{
    /// <summary>The picture types Explorer's slideshow takes (shell32's list).</summary>
    public static readonly IReadOnlySet<string> PictureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".bmp", ".dib", ".png", ".gif", ".jfif", ".jpe", ".tif", ".tiff", ".wdp", ".heic", ".heif",
        ".heics", ".heifs", ".hif", ".avci", ".avcs", ".avif", ".avifs", ".jxr", ".jxl", ".webp",
    };

    private readonly DispatcherQueueTimer _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
    private readonly SlideshowQueue _queue = new(Random.Shared);
    private bool _changing;

    public bool IsRunning { get; private set; }

    /// <summary>Re-reads the slideshow (at start, or after it was changed) and schedules its next picture.</summary>
    public void Refresh()
    {
        _timer.Stop();
        _queue.Clear();
        SlideshowSettings settings = SlideshowSettings.Read();
        IsRunning = settings.IsRunning;
        if (!IsRunning)
            return;

        // A picture from outside the slideshow (it was just set up) is replaced straight away.
        WallpaperSettings wallpaper = WallpaperSettings.Read();
        IReadOnlyList<string> pictures = ListPictures(settings.Sources);
        bool current = DisplayMonitor.GetAll().All(monitor => wallpaper.ImageFor(monitor.DevicePath) is { } path
            && pictures.Contains(path, StringComparer.OrdinalIgnoreCase));
        Schedule(current ? SlideshowSettings.NextChange(settings.Interval, DateTime.UtcNow, settings.LastTick, settings.AlignToMidnight) : 10);
    }

    /// <summary>The next picture now (<c>IDesktopWallpaper::AdvanceSlideshow</c>); throws when there is no slideshow.</summary>
    public void Advance()
    {
        if (!IsRunning)
            throw new InvalidOperationException("No slideshow");
        Change();
    }

    public void Dispose() => _timer.Stop();

    /// <summary>The pictures of the slideshow's folder (not its subfolders) in Explorer's name order, or its files.</summary>
    public static IReadOnlyList<string> ListPictures(IReadOnlyList<string> sources)
    {
        try
        {
            if (sources.Count == 1 && Directory.Exists(sources[0]))
            {
                return new DirectoryInfo(sources[0]).EnumerateFiles()
                    .Where(file => (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && PictureExtensions.Contains(file.Extension))
                    .Select(file => file.FullName)
                    .Order(Comparer<string>.Create((a, b) => ShellItems.CompareNames(Path.GetFileName(a), Path.GetFileName(b))))
                    .ToList();
            }
            return sources.Where(path => File.Exists(path) && PictureExtensions.Contains(Path.GetExtension(path))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not list the slideshow's pictures", ex);
            return [];
        }
    }

    private void Schedule(uint milliseconds)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.IsRepeating = false;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        // Explorer skips a change while the power plan pauses the slideshow (on battery by default).
        if (DesktopBackground.IsSlideshowPaused())
            Schedule(Reschedule(SlideshowSettings.Read()));
        else
            Change();
    }

    private async void Change()
    {
        if (_changing)
            return;
        _changing = true;
        try
        {
            SlideshowSettings settings = SlideshowSettings.Read();
            IsRunning = settings.IsRunning;
            if (!IsRunning)
                return;

            IReadOnlyList<string> pictures = await Task.Run(() => ListPictures(settings.Sources));
            IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
            WallpaperSettings wallpaper = WallpaperSettings.Read();
            var images = new List<(string MonitorPath, string Path)>();
            string? previous = monitors.Count > 0 ? wallpaper.ImageFor(monitors[^1].DevicePath) : null;
            foreach (DisplayMonitor monitor in monitors)
            {
                // Each monitor takes the picture after the previous one's (the first, after the last monitor's).
                if (_queue.Next(pictures, previous, settings.Shuffle) is not { } next)
                    break;
                images.Add((monitor.DevicePath, next));
                previous = next;
            }

            if (images.Count == monitors.Count && images.Count > 0)
            {
                Log.Info($"Slideshow: {string.Join(", ", images.Select(image => Path.GetFileName(image.Path)))}");
                await Task.Run(() => WallpaperRegistry.WriteMonitorImagesAsync(images, images.Count - 1));
                if (settings.Interval >= SlideshowSettings.PersistedTickInterval)
                    SlideshowSettings.WriteLastTick(DateTime.UtcNow);
                changed(settings.AnimationDuration);
                // As Explorer: Wallpaper names the transcoded copy; setting it tells everyone (us too) to re-read.
                DesktopBackground.SetWallpaper(WallpaperRegistry.TranscodedWallpaperPath);
            }
            Schedule(Reschedule(settings));
        }
        catch (Exception ex)
        {
            Log.Error("Changing the slideshow picture failed", ex);
        }
        finally
        {
            _changing = false;
        }
    }

    private static uint Reschedule(SlideshowSettings settings)
    {
        DateTime now = DateTime.UtcNow;
        return SlideshowSettings.NextChange(settings.Interval, now, now, settings.AlignToMidnight);
    }
}

/// <summary>
/// Explorer's order of slideshow pictures: in turn after the current one, or shuffled with the current one never
/// first; a round lasts until every picture has been shown.
/// </summary>
public sealed class SlideshowQueue(Random random)
{
    private readonly List<string> _pending = [];

    public void Clear() => _pending.Clear();

    public string? Next(IReadOnlyList<string> pictures, string? current, bool shuffle)
    {
        _pending.RemoveAll(picture => !pictures.Contains(picture));
        if (_pending.Count == 0)
            _pending.AddRange(NewRound(pictures, current, shuffle));
        if (_pending.Count == 0)
            return null;

        string next = _pending[0];
        _pending.RemoveAt(0);
        return next;
    }

    private IEnumerable<string> NewRound(IReadOnlyList<string> pictures, string? current, bool shuffle)
    {
        int position = current is null ? -1 : pictures.ToList().FindIndex(picture => string.Equals(picture, current, StringComparison.OrdinalIgnoreCase));
        if (!shuffle)
        {
            // The pictures after the current one; at the end, all but the current one from the start.
            if (position < 0)
                return pictures;
            return position + 1 < pictures.Count ? pictures.Skip(position + 1) : pictures.Where((_, i) => i != position);
        }

        var round = pictures.Where((_, i) => i != position).ToList();
        for (int i = round.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (round[i], round[j]) = (round[j], round[i]);
        }
        // The current picture comes round again, but not first.
        if (position >= 0 && round.Count > 0)
            round.Insert(random.Next(round.Count) + 1, pictures[position]);
        return round;
    }
}
