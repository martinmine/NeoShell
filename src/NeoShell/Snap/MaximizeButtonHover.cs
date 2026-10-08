using Microsoft.UI.Dispatching;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// Snap layouts on hovering a maximize button, as the shell (Windows leaves it to Explorer): the pointer resting on a
/// resizable window's maximize button for about 630 ms opens the flyout under it, and leaving both for 230 ms closes it,
/// as measured on Explorer. Windows with their own title bars count when they answer <c>WM_NCHITTEST</c> with
/// <c>HTMAXBUTTON</c> for theirs, as Windows asks them to.
/// </summary>
/// <remarks>
/// The pointer is followed on the thread pool: asking a window what's under the pointer waits for its thread, which
/// mustn't stall NeoShell's. The flyout itself opens and closes on the UI thread.
/// </remarks>
internal sealed class MaximizeButtonHover : IDisposable
{
    // Explorer's shows about 630 ms after the pointer stops on the button; opening and drawing the flyout takes the rest.
    private const int OpenDelay = 520;
    private const int CloseDelay = 230;
    // Physical pixels from the window's top within which the pointer may be over its buttons: tall custom title bars
    // included.
    private const int TitleBand = 120;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Func<SnapSettings> _settings;
    private readonly Action<nint, RectInt32> _open;
    private readonly Action _close;
    private readonly Timer _timer;
    private readonly Lock _lock = new();
    private int _polling;
    private nint _candidate;
    private long _since;
    private bool _enabled;
    // While the flyout is open: the button and the flyout (screen pixels), and when the pointer was last on either.
    private RectInt32? _button;
    private RectInt32? _flyout;
    private long _lastInside;

    /// <param name="open">Opens the flyout for the window under its button (UI thread).</param>
    /// <param name="close">Closes it (UI thread).</param>
    public MaximizeButtonHover(Func<SnapSettings> settings, Action<nint, RectInt32> open, Action close)
    {
        _settings = settings;
        _open = open;
        _close = close;
        _timer = new Timer(_ => Poll(), null, 50, 50);
    }

    /// <summary>The flyout opened for the button is at <paramref name="bounds"/> (UI thread).</summary>
    public void Shown(RectInt32 bounds)
    {
        lock (_lock)
            _flyout = bounds;
    }

    /// <summary>The flyout closed, whichever way.</summary>
    public void Closed()
    {
        lock (_lock)
        {
            _button = null;
            _flyout = null;
            _candidate = 0;
        }
    }

    public void Dispose()
    {
        // A poll under way finishes first: it may still queue work for the UI thread.
        using var stopped = new ManualResetEvent(false);
        if (_timer.Dispose(stopped))
            stopped.WaitOne(TimeSpan.FromSeconds(1));
    }

    private void Poll()
    {
        // A slow window can hold a poll up past the next tick.
        if (Interlocked.Exchange(ref _polling, 1) == 1)
            return;
        try
        {
            Follow();
        }
        catch (Exception ex)
        {
            Log.Error("Following the pointer for Snap layouts failed", ex);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void Follow()
    {
        long now = Environment.TickCount64;
        PointInt32 pointer = Cursor.Position();
        bool pressed = Cursor.IsButtonDown();
        lock (_lock)
        {
            if (_button is { } button)
            {
                bool onFlyout = _flyout is { } flyout && Contains(flyout, pointer);
                // A click anywhere else (the maximize button itself included) closes it at once.
                if (pressed && !onFlyout)
                {
                    CloseFlyout();
                    return;
                }
                if (onFlyout || Contains(button, pointer))
                    _lastInside = now;
                else if (now - _lastInside > CloseDelay)
                {
                    CloseFlyout();
                    return;
                }
                TopLevelWindows.HideCaptionTooltip(_candidate);
                return;
            }
        }

        if (pressed || OverMaximizeButton(pointer) is not { } over)
        {
            _candidate = 0;
            return;
        }
        (nint window, RectInt32 found) = over;
        if (window != _candidate)
        {
            // The setting is read once per hover, not on every poll.
            _candidate = window;
            _since = now;
            _enabled = _settings().HoverFlyout;
            return;
        }
        if (!_enabled)
            return;
        TopLevelWindows.HideCaptionTooltip(window);
        if (now - _since < OpenDelay)
            return;

        lock (_lock)
        {
            _button = found;
            _flyout = null;
            _lastInside = now;
        }
        _dispatcher.TryEnqueue(() => _open(window, found));
    }

    // The resizable window whose maximize button is under the pointer, and the button.
    private static (nint Window, RectInt32 Button)? OverMaximizeButton(PointInt32 pointer)
    {
        nint window = TopLevelWindows.RootAt(pointer);
        if (window == 0 || TopLevelWindows.GetProcessId(window) == Environment.ProcessId || TopLevelWindows.IsDesktop(window)
            || !TopLevelWindows.CanResize(window) || TopLevelWindows.IsMinimized(window))
        {
            return null;
        }
        RectInt32 visible = TopLevelWindows.GetVisibleBounds(window);
        if (pointer.Y - visible.Y >= TitleBand)
            return null;
        if (TopLevelWindows.CaptionMaximizeButton(window) is { } button)
            return Contains(button, pointer) ? (window, button) : null;
        return TopLevelWindows.AnswersMaximizeButton(window, pointer)
            ? (window, MaximizeButton.Around(pointer, point => TopLevelWindows.AnswersMaximizeButton(window, point)))
            : null;
    }

    private void CloseFlyout()
    {
        _button = null;
        _flyout = null;
        _candidate = 0;
        _dispatcher.TryEnqueue(() => _close());
    }

    private static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
}
