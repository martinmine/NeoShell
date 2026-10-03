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
        _window = new MessageWindow("NeoShell.ShellWindow", (_, _, _) => null, parent: 0, User32.WS_POPUP, User32.WS_EX_TOOLWINDOW);
        if (!User32.SetShellWindow(_window.Handle))
        {
            _window.Dispose();
            throw new InvalidOperationException("SetShellWindow failed: another shell is registered.");
        }
    }

    /// <summary>True when another process (normally Explorer) has registered itself as the shell.</summary>
    public static bool IsShellRunning() => User32.GetShellWindow() != 0;

    public void Dispose() => _window.Dispose();
}
