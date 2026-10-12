using System.ComponentModel;
using NeoShell.Interop.Windowing;

namespace NeoShell.Frames;

/// <summary>
/// The overlay thread for the windows of one app UI thread, with its own message loop and WinEvent hooks. Owning a
/// window joins our thread's input queue to the app thread's: with one thread per app thread, a hung app stalls only
/// its own frames, not every app's input.
/// </summary>
internal sealed class AppThread
{
    private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(500);

    private readonly uint _appThreadId;
    private readonly MessageLoop _loop;
    private readonly Action<nint> _ended;
    // On the overlay thread only.
    private readonly Dictionary<nint, LunaTracker> _trackers = [];
    private readonly List<WindowEvents> _events = [];

    /// <param name="ended">Called on the overlay thread when a window's frame ends by itself (the window was
    /// destroyed, or couldn't be owned).</param>
    public AppThread(uint appThreadId, Action<nint> ended)
    {
        _appThreadId = appThreadId;
        _ended = ended;
        _loop = MessageLoop.StartThread($"Luna frames of thread {appThreadId}");
        _loop.Post(Start);
    }

    public void Add(nint hwnd) => _loop.Post(() =>
    {
        try
        {
            _trackers[hwnd] = new LunaTracker(hwnd, _loop);
        }
        catch (Win32Exception ex)
        {
            FrameLog.Info($"Can't draw over window {hwnd:X} ({ex.Message}); leaving it alone");
            _ended(hwnd);
        }
    });

    public void Remove(nint hwnd) => _loop.Post(() =>
    {
        if (_trackers.Remove(hwnd, out LunaTracker? tracker))
            tracker.Dispose();
    });

    /// <summary>Puts every window back and ends the thread.</summary>
    public void Stop() => _loop.Post(() =>
    {
        foreach (WindowEvents events in _events)
            events.Dispose();
        foreach (LunaTracker tracker in _trackers.Values)
            tracker.Dispose();
        _trackers.Clear();
        _loop.Quit();
    });

    public bool Join(TimeSpan timeout) => _loop.Join(timeout);

    private void Start()
    {
        _events.Add(new WindowEvents(WindowEvent.Foreground, WindowEvent.Foreground));
        _events.Add(new WindowEvents(WindowEvent.MoveSizeStart, WindowEvent.MinimizeEnd, _appThreadId));
        _events.Add(new WindowEvents(WindowEvent.Destroyed, WindowEvent.Hidden, _appThreadId));
        _events.Add(new WindowEvents(WindowEvent.LocationChanged, WindowEvent.NameChanged, _appThreadId));
        _events.Add(new WindowEvents(WindowEvent.Cloaked, WindowEvent.Uncloaked, _appThreadId));
        foreach (WindowEvents events in _events)
            events.Raised += OnEvent;
        _loop.Every(s_poll, Poll);
    }

    private void OnEvent(WindowEvent kind, nint hwnd)
    {
        if (kind == WindowEvent.Foreground)
        {
            foreach (LunaTracker tracker in _trackers.Values)
            {
                tracker.CheckActive();
                tracker.CheckActiveSoon();
            }
            return;
        }
        if (!_trackers.TryGetValue(hwnd, out LunaTracker? found))
            return;
        if (kind == WindowEvent.Destroyed)
        {
            // The system hides overlays owned across threads with their owner, but leaves them to us to destroy.
            _trackers.Remove(hwnd);
            found.Dispose();
            _ended(hwnd);
            return;
        }
        found.OnEvent(kind);
    }

    /// <summary>
    /// Catches what comes without an event: the app stopping responding, and while it's in front, its caption's
    /// activation (see <see cref="LunaTracker.CheckActive"/>).
    /// </summary>
    private void Poll()
    {
        bool inFront = TopLevelWindows.GetThreadId(TopLevelWindows.GetForeground()) == _appThreadId;
        foreach (LunaTracker tracker in _trackers.Values)
        {
            tracker.CheckHung();
            if (inFront)
                tracker.CheckActive();
        }
    }
}
