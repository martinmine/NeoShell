using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>The <c>HSHELL_*</c> notifications NeoShell uses.</summary>
public enum ShellHookEvent
{
    WindowCreated = 1,
    WindowDestroyed = 2,
    WindowActivated = 4,
    /// <summary>A window's title or icon changed.</summary>
    Redraw = 6,
    WindowReplaced = 13,
    WindowReplacing = 14,
    /// <summary>A full-screen app was activated.</summary>
    RudeAppActivated = 0x8004,
    /// <summary>A window asks for attention (<c>FlashWindowEx</c>).</summary>
    Flash = 0x8006,
}

/// <summary>
/// Receives the shell hook messages Windows sends to taskbars: windows created, destroyed, activated, redrawn and
/// flashing. Create it on the UI thread; <see cref="Raised"/> is raised there.
/// </summary>
public sealed class ShellHook : IDisposable
{
    private readonly uint _shellHookMessage = User32.RegisterWindowMessage("SHELLHOOK");
    private readonly MessageWindow _window;

    public ShellHook()
    {
        _window = new MessageWindow("NeoShell.ShellHook", OnMessage);
        if (!User32.RegisterShellHookWindow(_window.Handle))
        {
            _window.Dispose();
            throw new InvalidOperationException("RegisterShellHookWindow failed.");
        }
    }

    /// <summary>The event and the window it is about.</summary>
    public event Action<ShellHookEvent, nint>? Raised;

    public void Dispose()
    {
        User32.DeregisterShellHookWindow(_window.Handle);
        _window.Dispose();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != _shellHookMessage)
            return null;

        if (Enum.IsDefined((ShellHookEvent)(int)wParam))
            Raised?.Invoke((ShellHookEvent)(int)wParam, lParam);
        return 0;
    }
}
