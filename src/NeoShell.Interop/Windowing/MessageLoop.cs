using System.Collections.Concurrent;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// A plain Win32 message loop with a way in: work posted from any thread, and timers, run on the loop's thread, as do
/// the messages of every window created there. For programs without WinUI's dispatcher.
/// </summary>
public sealed class MessageLoop
{
    private const uint WM_RUN = 0x8000 + 1; // WM_APP + 1

    private readonly MessageWindow _window;
    private readonly ConcurrentQueue<Action> _posted = new();
    // Only touched on the loop's thread.
    private readonly Dictionary<nuint, (Action Action, bool Repeat)> _timers = [];
    private nuint _lastTimer;
    private Thread? _thread;

    /// <summary>A loop on the calling thread; <see cref="Run"/> it there.</summary>
    public MessageLoop() => _window = new MessageWindow("NeoShell.MessageLoop", OnMessage);

    /// <summary>A loop on a new background thread of its own, already running.</summary>
    public static MessageLoop StartThread(string name)
    {
        MessageLoop? loop = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            loop = new MessageLoop();
            ready.Set();
            loop.Run();
        })
        { IsBackground = true, Name = name };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        loop!._thread = thread;
        return loop;
    }

    /// <summary>Runs <paramref name="action"/> on the loop's thread, after what was posted before it.</summary>
    public void Post(Action action)
    {
        _posted.Enqueue(action);
        User32.PostMessage(_window.Handle, WM_RUN, 0, 0);
    }

    /// <summary>Runs <paramref name="action"/> once after <paramref name="delay"/>. On the loop's thread only.</summary>
    public nuint After(TimeSpan delay, Action action) => StartTimer(delay, action, repeat: false);

    /// <summary>Runs <paramref name="action"/> every <paramref name="interval"/>. On the loop's thread only.</summary>
    public nuint Every(TimeSpan interval, Action action) => StartTimer(interval, action, repeat: true);

    public void Cancel(nuint timer)
    {
        if (_timers.Remove(timer))
            User32.KillTimer(_window.Handle, timer);
    }

    /// <summary>Dispatches messages until <see cref="Quit"/>.</summary>
    public void Run()
    {
        while (User32.GetMessage(out User32.MSG message, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(message);
            User32.DispatchMessage(message);
        }
    }

    /// <summary>Ends <see cref="Run"/>; work posted after this is dropped. On the loop's thread only.</summary>
    public void Quit()
    {
        _timers.Clear();
        _window.Dispose();
        User32.PostQuitMessage(0);
    }

    /// <summary>Waits for a loop started by <see cref="StartThread"/> to end.</summary>
    public bool Join(TimeSpan timeout) => _thread?.Join(timeout) ?? true;

    private nuint StartTimer(TimeSpan time, Action action, bool repeat)
    {
        nuint id = ++_lastTimer;
        _timers[id] = (action, repeat);
        User32.SetTimer(_window.Handle, id, (uint)Math.Max(1, time.TotalMilliseconds), 0);
        return id;
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WM_RUN)
        {
            while (_posted.TryDequeue(out Action? action))
                Invoke(action);
            return 0;
        }
        if (message == User32.WM_TIMER && _timers.TryGetValue((nuint)wParam, out var timer))
        {
            if (!timer.Repeat)
                Cancel((nuint)wParam);
            Invoke(timer.Action);
            return 0;
        }
        return null;
    }

    private static void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
    }
}
