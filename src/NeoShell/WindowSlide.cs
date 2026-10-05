using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell;

/// <summary>
/// Moves a window to new bounds frame by frame, as Windows 11 slides Start and the thumbnails out of the taskbar. The
/// window itself moves, not its content: a backdrop or DWM thumbnails belong to the window and wouldn't follow.
/// </summary>
internal sealed class WindowSlide(PinnedWindow placement)
{
    private (long Start, RectInt32 From, RectInt32 To, TimeSpan Duration, bool Decelerate, Action? Done)? _slide;

    /// <summary>
    /// Slides from where the window is to <paramref name="target"/> (taking its size at once), decelerating to a stop
    /// or accelerating away; replaces a slide under way, whose <c>done</c> then doesn't run.
    /// </summary>
    public void To(RectInt32 target, TimeSpan duration, bool decelerate, Action? done = null)
    {
        if (_slide is null)
            CompositionTarget.Rendering += Frame;
        _slide = (Stopwatch.GetTimestamp(), placement.Bounds, target, duration, decelerate, done);
    }

    public bool IsRunning => _slide is not null;

    /// <summary>Raised on each frame with the bounds the window is about to move to.</summary>
    public event Action<RectInt32>? Moving;

    public void Stop()
    {
        if (_slide is null)
            return;

        CompositionTarget.Rendering -= Frame;
        _slide = null;
    }

    private void Frame(object? sender, object e)
    {
        if (_slide is not { } slide)
            return;

        double progress = Math.Min(1, Stopwatch.GetElapsedTime(slide.Start) / slide.Duration);
        double eased = slide.Decelerate ? 1 - Math.Pow(1 - progress, 3) : Math.Pow(progress, 3);
        RectInt32 bounds = slide.To with
        {
            X = (int)Math.Round(slide.From.X + (slide.To.X - slide.From.X) * eased),
            Y = (int)Math.Round(slide.From.Y + (slide.To.Y - slide.From.Y) * eased),
        };
        Moving?.Invoke(bounds);
        placement.Bounds = bounds;
        if (progress < 1)
            return;

        Stop();
        slide.Done?.Invoke();
    }
}
