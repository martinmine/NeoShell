using Microsoft.UI.Dispatching;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Taskbar;

namespace NeoShell.Widgets;

/// <summary>A line of the event feed. <see cref="Kind"/> is a log level (INFO, WARN, ERROR) or OPEN, CLOSE, FOCUS.</summary>
internal sealed record FeedEntry(DateTime Time, string Kind, string Text);

/// <summary>
/// What NeoShell is doing, live: the lines it logs, and app windows opening, closing and coming to the front. Shared
/// by the event log widgets and kept while they come and go, so a new view starts with what came before.
/// </summary>
internal sealed class EventFeed : IDisposable
{
    public const int Length = 200;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly WindowTracker _tracker;
    private readonly Queue<FeedEntry> _entries = new();
    private Dictionary<nint, string> _windows = [];
    private nint _foreground;

    public EventFeed(WindowTracker tracker)
    {
        _tracker = tracker;
        foreach (LogEntry entry in Log.Recent())
            Add(new FeedEntry(entry.Time, entry.Level, entry.Message));
        _windows = AppWindows();
        _foreground = tracker.Foreground;

        Log.Written += OnLogWritten;
        tracker.Changed += OnWindowsChanged;
        tracker.ForegroundChanged += OnForegroundChanged;
    }

    /// <summary>The entries, oldest first.</summary>
    public IEnumerable<FeedEntry> Entries => _entries;

    /// <summary>An entry was added, on the UI thread.</summary>
    public event Action<FeedEntry>? Added;

    public void Dispose()
    {
        Log.Written -= OnLogWritten;
        _tracker.Changed -= OnWindowsChanged;
        _tracker.ForegroundChanged -= OnForegroundChanged;
    }

    // Any thread logs.
    private void OnLogWritten(LogEntry entry) =>
        _dispatcher.TryEnqueue(() => Add(new FeedEntry(entry.Time, entry.Level, entry.Message)));

    private void OnWindowsChanged()
    {
        Dictionary<nint, string> now = AppWindows();
        foreach ((nint hwnd, string name) in now)
        {
            if (!_windows.ContainsKey(hwnd))
                Add(new FeedEntry(DateTime.Now, "OPEN", name));
        }
        foreach ((nint hwnd, string name) in _windows)
        {
            if (!now.ContainsKey(hwnd))
                Add(new FeedEntry(DateTime.Now, "CLOSE", name));
        }
        _windows = now;
    }

    // Also raised while a window is moved; only a new foreground window counts.
    private void OnForegroundChanged()
    {
        nint foreground = _tracker.Foreground;
        if (foreground == _foreground)
            return;
        _foreground = foreground;
        if (_windows.TryGetValue(foreground, out string? name))
            Add(new FeedEntry(DateTime.Now, "FOCUS", name));
    }

    /// <summary>The windows with a taskbar button, by handle: their process and title.</summary>
    private Dictionary<nint, string> AppWindows() =>
        _tracker.Windows
            .Where(window => TaskFilter.GetsButton(window, Environment.ProcessId))
            .ToDictionary(window => window.Handle, Describe);

    private static string Describe(WindowInfo window)
    {
        string process = Path.GetFileName(window.ProcessPath) ?? "?";
        return window.Title.Length > 0 ? $"{process} — {window.Title}" : process;
    }

    private void Add(FeedEntry entry)
    {
        _entries.Enqueue(entry);
        if (_entries.Count > Length)
            _entries.Dequeue();
        Added?.Invoke(entry);
    }
}
