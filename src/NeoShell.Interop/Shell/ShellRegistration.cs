using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Registers NeoShell as the shell: while this exists, <c>GetShellWindow</c> returns its window. Windows uses that to
/// tell whether a shell is running; Explorer started meanwhile opens a folder window instead of a second desktop and
/// taskbar. Disposing it (or the process ending) gives the role up.
/// </summary>
public sealed class ShellRegistration : IDisposable
{
    private readonly MessageWindow _window;

    public ShellRegistration()
    {
        // Hidden but top-level: session-end messages only go to top-level windows.
        _window = new MessageWindow("NeoShell.ShellWindow", OnMessage, parent: 0, User32.WS_POPUP, User32.WS_EX_TOOLWINDOW);
        if (!User32.SetShellWindow(_window.Handle))
        {
            _window.Dispose();
            throw new InvalidOperationException("SetShellWindow failed: another shell is registered.");
        }
    }

    /// <summary>Windows asks for the task list: Ctrl+Esc, which Explorer answers with Start.</summary>
    public event Action? TaskListRequested;

    /// <summary>The session is ending (sign-out, restart, shutdown). Raised before Windows ends the process.</summary>
    public event Action? SessionEnding;


    /// <summary>True when another process (normally Explorer) has registered itself as the shell.</summary>
    public static bool IsShellRunning() => User32.GetShellWindow() != 0;

    /// <summary>True while Windows is signing out, restarting or shutting down.</summary>
    public static bool IsSessionEnding() => User32.GetSystemMetrics(User32.SM_SHUTTINGDOWN) != 0;

    /// <summary>
    /// Finishes taking over as the shell: becomes the task manager window (which receives Ctrl+Esc) and tells
    /// Windows the shell is up, so the sign-in screen gives way to the desktop.
    /// </summary>
    public void Complete()
    {
        User32.SetTaskmanWindow(_window.Handle);
        foreach (string name in (string[])["Local\\ShellDesktopSwitchEvent", "msgina: ShellReadyEvent"])
        {
            // Which of these exist depends on how the session started; signalling a missing one is a no-op.
            nint readyEvent = Kernel32.OpenEvent(Kernel32.EVENT_MODIFY_STATE, false, name);
            if (readyEvent != 0)
            {
                Kernel32.SetEvent(readyEvent);
                Kernel32.CloseHandle(readyEvent);
            }
        }
    }

    public void Dispose() => _window.Dispose();

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case User32.WM_SYSCOMMAND when (wParam & 0xFFF0) == User32.SC_TASKLIST:
                TaskListRequested?.Invoke();
                return 0;
            case User32.WM_QUERYENDSESSION:
                return 1; // never blocks the session from ending
            case User32.WM_ENDSESSION when wParam != 0:
                SessionEnding?.Invoke();
                return 0;

            default:
                return null;
        }
    }
}
