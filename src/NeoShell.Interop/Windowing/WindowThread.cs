using Microsoft.Win32.SafeHandles;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>Learns when the thread that owns a window ends, e.g. its app crashed or was killed.</summary>
public sealed class WindowThread : IDisposable
{
    private readonly ThreadHandle _thread;
    private readonly RegisteredWaitHandle _wait;

    private WindowThread(ThreadHandle thread, Action ended)
    {
        _thread = thread;
        _wait = ThreadPool.RegisterWaitForSingleObject(thread, (_, _) => ended(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>
    /// Calls <paramref name="ended"/> on the thread pool once the thread that owns <paramref name="hwnd"/> ends; null
    /// if the thread can't be watched (it's gone already, or belongs to a more privileged app).
    /// </summary>
    public static WindowThread? Watch(nint hwnd, Action ended)
    {
        uint threadId = User32.GetWindowThreadProcessId(hwnd, out _);
        nint handle = threadId == 0 ? 0 : Kernel32.OpenThread(Kernel32.SYNCHRONIZE, false, threadId);
        if (handle == 0)
            return null;
        return new WindowThread(new ThreadHandle(handle), ended);
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _thread.Dispose();
    }

    /// <summary>A thread handle, which is signalled when the thread ends.</summary>
    private sealed class ThreadHandle : WaitHandle
    {
        public ThreadHandle(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }
}
