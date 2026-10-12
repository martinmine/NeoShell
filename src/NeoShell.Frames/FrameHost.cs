using NeoShell.Interop.Windowing;

namespace NeoShell.Frames;

/// <summary>
/// Finds the windows to draw Luna over, by NeoShell's settings, and hands each to the overlay thread of its app thread
/// (<see cref="AppThread"/>). Runs on the program's main thread; disposing it puts every window back.
/// </summary>
internal sealed class FrameHost : IDisposable
{
    private static readonly TimeSpan s_settingsRetry = TimeSpan.FromMilliseconds(250);

    private readonly MessageLoop _loop;
    private readonly string _settingsPath;
    private readonly WindowEvents _shown = new(WindowEvent.Shown, WindowEvent.Shown);
    private readonly WindowEvents _uncloaked = new(WindowEvent.Uncloaked, WindowEvent.Uncloaked);
    private readonly Dictionary<uint, AppThread> _threads = [];
    // Each window drawn over, and its app thread.
    private readonly Dictionary<nint, uint> _windows = [];
    private readonly int _ownProcessId = Environment.ProcessId;
    private HostSettings _settings = new();

    public FrameHost(MessageLoop loop, string settingsPath)
    {
        _loop = loop;
        _settingsPath = settingsPath;
        PutBackLeftovers();
        _shown.Raised += (_, hwnd) => Consider(hwnd);
        _uncloaked.Raised += (_, hwnd) => Consider(hwnd);
        Reload();
    }

    /// <summary>Reads the settings again: frames the windows they now name and puts back the ones they no longer do.</summary>
    public void Reload()
    {
        if (HostSettings.Load(_settingsPath) is not { } settings)
        {
            _loop.After(s_settingsRetry, Reload);
            return;
        }
        _settings = settings;
        foreach (nint hwnd in _windows.Keys.Where(hwnd => !ShouldDraw(hwnd)).ToList())
            Forget(hwnd, putBack: true);
        foreach (nint hwnd in TopLevelWindows.GetAll())
            Consider(hwnd);
        FrameLog.Info($"Drawing Luna over {_windows.Count} windows of {_threads.Count} app threads");
    }

    public void Dispose()
    {
        _shown.Dispose();
        _uncloaked.Dispose();
        foreach (AppThread thread in _threads.Values)
            thread.Stop();
        // An overlay thread joined to a hung app's input may take a while; the process ends anyway, its windows with it.
        foreach (AppThread thread in _threads.Values)
            thread.Join(TimeSpan.FromSeconds(2));
        FrameLog.Info($"Put {_windows.Count} windows back");
        _threads.Clear();
        _windows.Clear();
    }

    /// <summary>
    /// Takes Luna's attributes off the windows an earlier run left them on: if it crashed or was ended, its overlays
    /// went with it, but the app windows kept the blue caption colour.
    /// </summary>
    private static void PutBackLeftovers()
    {
        foreach (nint hwnd in TopLevelWindows.GetAll().Where(FrameProgram.IsMarkedLuna))
            FrameProgram.PutBack(hwnd);
    }

    private bool ShouldDraw(nint hwnd)
    {
        WindowFrame.Target window = WindowFrame.Read(hwnd);
        return FrameRules.CanDrawOver(window, _ownProcessId) && _settings.DrawsLuna(window.ProcessName, window.ClassName);
    }

    private void Consider(nint hwnd)
    {
        if (_windows.ContainsKey(hwnd) || !ShouldDraw(hwnd))
            return;
        uint threadId = TopLevelWindows.GetThreadId(hwnd);
        if (threadId == 0)
            return;
        if (!_threads.TryGetValue(threadId, out AppThread? thread))
            _threads[threadId] = thread = new AppThread(threadId, ended => _loop.Post(() => Forget(ended, putBack: false)));
        _windows[hwnd] = threadId;
        thread.Add(hwnd);
    }

    /// <summary>Stops drawing over a window; an app thread with no windows left loses its overlay thread.</summary>
    private void Forget(nint hwnd, bool putBack)
    {
        if (!_windows.Remove(hwnd, out uint threadId))
            return;
        AppThread thread = _threads[threadId];
        if (putBack)
            thread.Remove(hwnd);
        if (!_windows.ContainsValue(threadId))
        {
            _threads.Remove(threadId);
            thread.Stop();
        }
    }
}
